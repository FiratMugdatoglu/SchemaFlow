using SchemaFlow.Core.Models;
using SchemaFlow.Core.Models.Operations;
using SchemaFlow.Core.Parsers.Sql;
using System.Text.RegularExpressions;

namespace SchemaFlow.Core.Services;

/// <summary>
/// Normalize edilmiş MigrationOperation modelleri üzerinde offline statik güvenlik ve risk analizini yürüten motordur.
/// Yanlış pozitifleri azaltmak için kesin tespit (Confirmed) ile olasılık çıkarımını (Inferred) ve belirsizliği (Unknown) ayırır.
/// Hiçbir SQL ifadesini salt varlığından ötürü güvenli ilan etmez.
/// </summary>
public class MigrationRiskEngine
{
    public MigrationSafetyReport Analyze(IEnumerable<MigrationOperation> operations, string migrationName = "Migration")
    {
        var opList = operations.ToList();
        var report = new MigrationSafetyReport
        {
            TargetMigrationName = migrationName,
            Operations = opList
        };

        // Eğer operasyonlar sadece Down metoduna aitse Down analiz edilir, 
        // aksi takdirde Up (ve scope belirtilmemiş genel) operasyonlar analiz edilir.
        var isDownAnalysis = opList.Count > 0 && opList.All(o => o.Scope == MigrationMethodScope.Down);
        var targetOps = isDownAnalysis
            ? opList.Where(o => o.Scope == MigrationMethodScope.Down).ToList()
            : opList.Where(o => o.Scope != MigrationMethodScope.Down).ToList();

        // 1. Tablo bazlı gruplandırma ve kontroller
        var tableGroups = targetOps.Where(o => !string.IsNullOrEmpty(o.TableName))
                                   .GroupBy(o => o.TableName, StringComparer.OrdinalIgnoreCase);

        var rawSqls = targetOps.OfType<RawSqlOperation>().ToList();

        foreach (var group in tableGroups)
        {
            var tableName = group.Key;
            var tableOps = group.ToList();

            // A) Tablo Silme Kontrolü (DropTable)
            foreach (var dt in tableOps.OfType<DropTableOperation>())
            {
                report.Issues.Add(new MigrationRiskIssue
                {
                    RuleId = "MIG001",
                    Severity = RiskSeverity.Critical,
                    Detection = dt.Detection,
                    Confidence = dt.Confidence,
                    TableName = tableName,
                    Title = $"Kritik Veri Kaybı: '{tableName}' Tablosu Tamamen Siliniyor",
                    Description = $"Migration içerisinde '{tableName}' tablosu DropTable ile kaldırılıyor.",
                    ImpactAnalysis = "Canlı veritabanındaki tüm satırlar, ilişkili yabancı anahtarlar ve indeksler geri dönüşsüz olarak silinecektir.",
                    Recommendation = "Tablonun önceden yedeklendiğinden ve canlı uygulamanın bu tabloya bağımlı olmadığından emin olun.",
                    LineNumber = dt.LineNumber,
                    SourceFile = dt.SourceFile
                });
            }

            // B) Tablo Yeniden Adlandırma Kontrolü (RenameTable)
            foreach (var rt in tableOps.OfType<RenameTableOperation>())
            {
                report.Issues.Add(new MigrationRiskIssue
                {
                    RuleId = "MIG007",
                    Severity = RiskSeverity.High,
                    Detection = rt.Detection,
                    Confidence = rt.Confidence,
                    TableName = tableName,
                    Title = $"Uygulama Uyumluluk Riski: '{tableName}' ➡️ '{rt.NewTableName}' Tablo Adı Değişimi",
                    Description = $"'{tableName}' tablosu '{rt.NewTableName}' olarak yeniden adlandırılıyor.",
                    ImpactAnalysis = "Sıfır kesintili (zero-downtime) veya rolling deployment sırasında eski kod sürümünü çalıştıran uygulama podları sorgularda hata alacaktır.",
                    Recommendation = "Expand & Contract kapsamında önce view/alias veya çift yazma desteği oluşturup ardından aşamalı geçiş yapın.",
                    LineNumber = rt.LineNumber,
                    SourceFile = rt.SourceFile
                });
            }

            // C) Expand & Contract ve Sütun Silme/Ekleme Analizi (Muhafazakar Eşleme)
            var droppedCols = tableOps.OfType<DropColumnOperation>().ToList();
            var addedCols = tableOps.OfType<AddColumnOperation>().ToList();

            EvaluateColumnDropsAndAdditions(tableName, droppedCols, addedCols, rawSqls, report, targetOps);

            // D) Güvensiz NOT NULL Sütun Eklenmesi
            foreach (var add in addedCols)
            {
                if (add.NullableState == false && string.IsNullOrEmpty(add.DefaultValue) && string.IsNullOrEmpty(add.DefaultValueSql))
                {
                    report.Issues.Add(new MigrationRiskIssue
                    {
                        RuleId = "MIG004",
                        Severity = RiskSeverity.Critical,
                        Detection = add.Detection,
                        Confidence = add.Confidence,
                        TableName = tableName,
                        ColumnName = add.ColumnName,
                        Title = $"Canlıyı Patlatabilecek Ekleme: Varsayılan Değersiz 'NOT NULL {add.ColumnName}'",
                        Description = $"'{tableName}' tablosuna varsayılan değeri (defaultValue / defaultValueSql) olmayan NOT NULL sütun ekleniyor.",
                        ImpactAnalysis = "Eğer canlı veritabanında bu tabloda en az 1 satır bile varsa, migration veritabanı kısıtlama hatası vererek (SQL error) çöker.",
                        Recommendation = "Sütunu nullable yapın veya bir defaultValue / defaultValueSql (örn: default 0 veya '') tanımlayın.",
                        LineNumber = add.LineNumber,
                        SourceFile = add.SourceFile
                    });
                }
                else if (add.NullableState == null)
                {
                    // Dinamik / çözülemeyen nullability
                    report.Issues.Add(new MigrationRiskIssue
                    {
                        RuleId = "MIG004_UNKNOWN",
                        Severity = RiskSeverity.Medium,
                        Detection = DetectionType.Unknown,
                        Confidence = AnalysisConfidence.Low,
                        TableName = tableName,
                        ColumnName = add.ColumnName,
                        Title = $"Belirsiz Nullability: '{tableName}.{add.ColumnName}'",
                        Description = $"'{tableName}.{add.ColumnName}' sütununun nullability değeri dinamik bir ifade olduğu için statik olarak çözülemedi.",
                        ImpactAnalysis = "Çalışma zamanında bu sütun NOT NULL olarak değerlendirilirse ve tabloda satır varsa migration çökebilir.",
                        Recommendation = "Sütun tanımında açık boolean (true/false) veya güvenli default değer kullanıldığından emin olun.",
                        LineNumber = add.LineNumber,
                        SourceFile = add.SourceFile
                    });
                }
            }

            // E) AlterColumn Kontrolleri
            foreach (var alter in tableOps.OfType<AlterColumnOperation>())
            {
                EvaluateAlterColumn(tableName, alter, report);
            }

            // F) Sütun Yeniden Adlandırma (RenameColumn)
            foreach (var rc in tableOps.OfType<RenameColumnOperation>())
            {
                report.Issues.Add(new MigrationRiskIssue
                {
                    RuleId = "MIG006",
                    Severity = RiskSeverity.Medium,
                    Detection = rc.Detection,
                    Confidence = rc.Confidence,
                    TableName = tableName,
                    ColumnName = rc.ColumnName,
                    Title = $"Sıfır Kesinti Riski: '{tableName}.{rc.ColumnName}' ➡️ '{rc.NewColumnName}' Yeniden Adlandırma",
                    Description = $"'{rc.ColumnName}' sütunu '{rc.NewColumnName}' olarak adlandırılıyor.",
                    ImpactAnalysis = "Canlıda çalışan eski uygulama sürümleri eski sütunu sorgulamaya devam ediyorsa kesinti yaşanabilir.",
                    Recommendation = "Zero-downtime rolling deployment yapıyorsanız Expand & Contract desenini tercih edin.",
                    LineNumber = rc.LineNumber,
                    SourceFile = rc.SourceFile
                });
            }

            // G) Primary Key Değişiklikleri
            foreach (var pk in tableOps.OfType<DropPrimaryKeyOperation>())
            {
                report.Issues.Add(new MigrationRiskIssue
                {
                    RuleId = "MIG008",
                    Severity = RiskSeverity.High,
                    Detection = pk.Detection,
                    Confidence = pk.Confidence,
                    TableName = tableName,
                    Title = $"Birincil Anahtar Kaldırılıyor: '{pk.ConstraintName}' on '{tableName}'",
                    Description = $"'{tableName}' tablosunun birincil anahtarı kaldırılıyor.",
                    ImpactAnalysis = "Tablo kilitlenmesine ve tekillik garantisinin kaybolmasına neden olabilir.",
                    Recommendation = "Yeni bir birincil anahtar tanımlandığından emin olun.",
                    LineNumber = pk.LineNumber,
                    SourceFile = pk.SourceFile
                });
            }

            // H) Foreign Key Değişiklikleri
            foreach (var fk in tableOps.OfType<DropForeignKeyOperation>())
            {
                report.Issues.Add(new MigrationRiskIssue
                {
                    RuleId = "MIG009",
                    Severity = RiskSeverity.Medium,
                    Detection = fk.Detection,
                    Confidence = fk.Confidence,
                    TableName = tableName,
                    Title = $"Yabancı Anahtar Siliniyor: '{fk.ConstraintName}' from '{tableName}'",
                    Description = $"'{tableName}' tablosundan bir yabancı anahtar kaldırılıyor.",
                    ImpactAnalysis = "İlişkisel bütünlük ve CASCADE koruması kalkacaktır.",
                    Recommendation = "İlişkinin uygulama katmanında mı yoksa farklı bir mimaride mi yönetileceğini doğrulayın.",
                    LineNumber = fk.LineNumber,
                    SourceFile = fk.SourceFile
                });
            }
        }

        // 2. Ham SQL Operasyonları ve Çoklu Komut Analizi
        foreach (var sqlOp in rawSqls)
        {
            var statements = SqlScriptLexer.SplitAndAnalyzeStatements(sqlOp.SqlContent);

            // Her SQL komutunu bağımsız denetle; TRUNCATE, DROP, DELETE gibi yıkıcı komutları kaçırma!
            foreach (var stmt in statements)
            {
                if (stmt.IsDestructive)
                {
                    var ruleId = stmt.CommandKind switch
                    {
                        SqlCommandKind.Truncate => "MIG009_SQL_TRUNCATE",
                        SqlCommandKind.Drop => "MIG009_SQL_DROP",
                        SqlCommandKind.Delete => "MIG009_SQL_DELETE",
                        SqlCommandKind.Alter => "MIG009_SQL_ALTER",
                        _ => "MIG009_SQL_DESTRUCTIVE"
                    };

                    var severity = (stmt.CommandKind == SqlCommandKind.Delete && stmt.HasWhereClause)
                        ? RiskSeverity.High
                        : RiskSeverity.Critical;

                    report.Issues.Add(new MigrationRiskIssue
                    {
                        RuleId = ruleId,
                        Severity = severity,
                        Detection = DetectionType.Confirmed,
                        Confidence = AnalysisConfidence.High,
                        TableName = stmt.TargetTable ?? "RawSQL",
                        Title = $"🔴 Yıkıcı SQL Komutu: {stmt.CommandKind.ToString().ToUpperInvariant()}",
                        Description = stmt.DestructiveReason ?? $"Ham SQL içerisinde '{stmt.CleanSql}' komutu tespit edildi.",
                        ImpactAnalysis = "Bu SQL komutu çalıştırıldığında veritabanı tablolarında kalıcı veri kaybına yol açacaktır.",
                        Recommendation = "Yıkıcı SQL komutlarını migration sürecinden çıkarın veya veri yedeği alarak kontrollü çalıştırın.",
                        LineNumber = sqlOp.LineNumber,
                        SourceFile = sqlOp.SourceFile
                    });
                }
            }

            // Genel SQL çağrısı bilgilendirmesi
            report.Issues.Add(new MigrationRiskIssue
            {
                RuleId = "MIG010",
                Severity = RiskSeverity.Medium,
                Detection = DetectionType.Confirmed,
                Confidence = AnalysisConfidence.High,
                TableName = "RawSQL",
                Title = $"Doğrudan Ham SQL Çalıştırılıyor (Satır {sqlOp.LineNumber})",
                Description = $"Migration içerisinde 'migrationBuilder.Sql(...)' çağrısı tespit edildi ({sqlOp.SqlContent.Length} karakter, {statements.Count} ifade).",
                ImpactAnalysis = "Çevrimdışı statik analiz keyfi SQL komutlarının veri güvenliğini ve kilitlenme etkilerini garanti edemez.",
                Recommendation = "Çalıştırılan SQL komutunun idempotent (tekrar çalıştırılabilir) ve güvenli olduğunu manuel inceleyin.",
                LineNumber = sqlOp.LineNumber,
                SourceFile = sqlOp.SourceFile
            });
        }

        // 3. Bilinmeyen / Çözülemeyen Operasyonlar Kontrolü (UnknownOperation)
        var unknownOps = opList.OfType<UnknownOperation>().ToList();
        foreach (var unk in unknownOps)
        {
            report.Issues.Add(new MigrationRiskIssue
            {
                RuleId = "MIG012",
                Severity = RiskSeverity.Low,
                Detection = DetectionType.Unknown,
                Confidence = AnalysisConfidence.Low,
                TableName = unk.TableName,
                Title = $"İncelenemeyen Operasyon: '{unk.MethodName}' (Satır {unk.LineNumber})",
                Description = unk.Reason,
                ImpactAnalysis = "Statik analiz bu metot çağrısının şemaya etkisini çözemedi.",
                Recommendation = "Özel migration metotlarını veya yardımcı sınıfları manuel inceleyin.",
                LineNumber = unk.LineNumber,
                SourceFile = unk.SourceFile
            });
        }

        return report;
    }

    private static void EvaluateColumnDropsAndAdditions(
        string tableName,
        List<DropColumnOperation> droppedCols,
        List<AddColumnOperation> addedCols,
        List<RawSqlOperation> rawSqls,
        MigrationSafetyReport report,
        List<MigrationOperation>? scopeOps = null)
    {
        var availableAdditions = new List<AddColumnOperation>(addedCols);

        foreach (var drop in droppedCols)
        {
            // Muhafazakar Eşleme: Sadece benzer isim ve uyumlu tipteki kolonları rename adayı say
            var match = FindBestMatchingRenameCandidate(drop, availableAdditions);

            if (match != null)
            {
                availableAdditions.Remove(match);

                // SQL Backfill denetimi ve sıra kontrolü yap
                var backfillResult = AnalyzeSqlBackfill(tableName, drop, match, rawSqls, scopeOps);

                switch (backfillResult.Status)
                {
                    case BackfillStatus.ValidCandidateOrderVerified:
                        // Asla tamamen güvenli denilmez; Inferred risk korunur ve çalışma zamanı teyidi istenir
                        report.Issues.Add(new MigrationRiskIssue
                        {
                            RuleId = "MIG003",
                            Severity = RiskSeverity.High,
                            Detection = DetectionType.Inferred,
                            Confidence = AnalysisConfidence.Medium,
                            TableName = tableName,
                            ColumnName = drop.ColumnName,
                            Title = $"Veri Taşımalı Sütun Değişimi Adayı (Doğrulama Gerekli): '{drop.ColumnName}' ➡️ '{match.ColumnName}'",
                            Description = $"'{tableName}' tablosunda '{drop.ColumnName}' silinip yerine '{match.ColumnName}' ekleniyor. Yapısal bir UPDATE komutu bulundu ve çalışma sırası (Add -> UPDATE -> Drop) doğrulandı.",
                            ImpactAnalysis = "Çevrimdışı statik analiz tüm satırların eksiksiz kopyalandığını, veri tipi taşması olmadığını ve migration sırasında kilitlenme yaşanmayacağını garanti edemez.",
                            Recommendation = "Canlıya almadan önce veritabanı staging ortamında satır sayısı doğrulaması (Row Count Verification) yapınız.",
                            LineNumber = drop.LineNumber,
                            SourceFile = drop.SourceFile
                        });
                        break;

                    case BackfillStatus.InvalidExecutionOrder:
                        // Sütun silindikten SONRA veya eklenmeden ÖNCE SQL komutu çalıştırılmış! Canlıda çöker!
                        report.Issues.Add(new MigrationRiskIssue
                        {
                            RuleId = "MIG003_ORDER",
                            Severity = RiskSeverity.Critical,
                            Detection = DetectionType.Confirmed,
                            Confidence = AnalysisConfidence.High,
                            TableName = tableName,
                            ColumnName = drop.ColumnName,
                            Title = $"🔴 Hatalı Operasyon Sırası: '{tableName}' Tablosunda Güvensiz SQL Sıralaması",
                            Description = $"{backfillResult.Detail}. Bu migration canlı veritabanında sütun bulunamadı (Column Not Found) hatası ile çökecektir.",
                            ImpactAnalysis = "SQL komutu çalıştırıldığında kaynak sütun silinmiş veya hedef sütun henüz oluşturulmamış olacağından deployment derhal başarısız olur.",
                            Recommendation = "Operasyon sırasını düzeltin: 1. AddColumn, 2. UPDATE SQL, 3. DropColumn.",
                            LineNumber = backfillResult.SqlLineNumber > 0 ? backfillResult.SqlLineNumber : drop.LineNumber,
                            SourceFile = drop.SourceFile
                        });
                        break;

                    case BackfillStatus.DestructiveSideEffect:
                        // UPDATE adayı tespit edilse dahi aynı SQL çağrısı içinde TRUNCATE / DROP gibi yıkıcı komut var!
                        report.Issues.Add(new MigrationRiskIssue
                        {
                            RuleId = "MIG003",
                            Severity = RiskSeverity.Critical,
                            Detection = DetectionType.Confirmed,
                            Confidence = AnalysisConfidence.High,
                            TableName = tableName,
                            ColumnName = drop.ColumnName,
                            Title = $"🔴 Yıkıcı Yan Etki İçeren SQL: '{drop.ColumnName}' Taşıması Güvensiz",
                            Description = $"'{tableName}' tablosunda UPDATE veri taşıma adayı bulundu ancak aynı SQL bloğu içerisinde yıkıcı ek komutlar mevcut ({backfillResult.Detail}).",
                            ImpactAnalysis = "SQL komutu veri taşımaya çalışırken diğer tablolarda geri dönüşsüz veri kaybına neden olacaktır.",
                            Recommendation = "Veri taşıma UPDATE komutunu yıkıcı ek komutlardan (TRUNCATE, DROP) ayırın.",
                            LineNumber = backfillResult.SqlLineNumber > 0 ? backfillResult.SqlLineNumber : drop.LineNumber,
                            SourceFile = drop.SourceFile
                        });
                        break;

                    case BackfillStatus.SelectOrCommentOnly:
                    case BackfillStatus.PartialOrConditional:
                    case BackfillStatus.None:
                    default:
                        // Veri taşıması yok veya geçersiz SQL
                        report.Issues.Add(new MigrationRiskIssue
                        {
                            RuleId = "MIG003",
                            Severity = RiskSeverity.Critical,
                            Detection = DetectionType.Inferred,
                            Confidence = AnalysisConfidence.High,
                            TableName = tableName,
                            ColumnName = drop.ColumnName,
                            Title = $"🔴 Potansiyel Veri Kaybı (Expand & Contract İhlali): '{drop.ColumnName}' ➡️ '{match.ColumnName}'",
                            Description = $"'{tableName}' tablosunda '{drop.ColumnName}' silinirken aynı anda '{match.ColumnName}' ekleniyor. Geçerli bir UPDATE veri taşıma komutu tespit edilemedi ({backfillResult.Detail}).",
                            ImpactAnalysis = $"'{drop.ColumnName}' sütunundaki tüm mevcut canlı veriler geri dönüşsüz olarak silinecektir.",
                            Recommendation = "Veri kaybını önlemek için Expand & Contract adımlarını uygulayın veya RenameColumn kullanın.",
                            LineNumber = drop.LineNumber,
                            SourceFile = drop.SourceFile
                        });

                        report.ExpandContractPlans.Add(new ExpandContractPlan
                        {
                            TableName = tableName,
                            DroppedColumn = drop.ColumnName,
                            AddedColumn = match.ColumnName,
                            Reason = $"'{tableName}' tablosunda '{drop.ColumnName}' silinip yerine '{match.ColumnName}' eklenmiş. Arada verileri eksiksiz kopyalayan geçerli bir SQL bulunamadığı için canlı ortamda mevcut veriler silinecektir.",
                            PhaseSteps = new List<string>
                            {
                                $"Aşama 1 (Expand): '{match.ColumnName}' sütununu nullable veya güvenli varsayılan değer ile canlıya ekleyin.",
                                $"Aşama 2 (Backfill): Verileri kopyalayın: UPDATE [{tableName}] SET [{match.ColumnName}] = [{drop.ColumnName}] WHERE [{match.ColumnName}] IS NULL;",
                                $"Aşama 3 (Migrate): Uygulamanızı yeni '{match.ColumnName}' sütununu kullanacak şekilde canlıya deploy edin.",
                                $"Aşama 4 (Contract): Bir sonraki sürümde eski '{drop.ColumnName}' sütununu güvenle Drop edin.",
                                $"Hızlı Alternatif: Eğer kesintili bakım penceresi varsa 'migrationBuilder.RenameColumn(name: \"{drop.ColumnName}\", newName: \"{match.ColumnName}\", table: \"{tableName}\");' kullanabilirsiniz."
                            },
                            SuggestedSqlBackfill = $"UPDATE [{tableName}] SET [{match.ColumnName}] = [{drop.ColumnName}] WHERE [{match.ColumnName}] IS NULL;",
                            SuggestedEfCoreCode = $"migrationBuilder.RenameColumn(name: \"{drop.ColumnName}\", newName: \"{match.ColumnName}\", table: \"{tableName}\");"
                        });
                        break;
                }
            }
            else
            {
                // Bağımsız Sütun Silinmesi (İlişkisiz AddColumn ile zorla eşleştirilmez)
                report.Issues.Add(new MigrationRiskIssue
                {
                    RuleId = "MIG002",
                    Severity = RiskSeverity.High,
                    Detection = drop.Detection,
                    Confidence = drop.Confidence,
                    TableName = tableName,
                    ColumnName = drop.ColumnName,
                    Title = $"Kalıcı Veri Kaybı: '{tableName}.{drop.ColumnName}' Sütunu Siliniyor",
                    Description = $"Migration içerisinde '{tableName}.{drop.ColumnName}' sütunu DropColumn ile siliniyor.",
                    ImpactAnalysis = "Bu sütundaki tüm geçmiş veriler kalıcı olarak silinecektir. Canlıda çalışan eski uygulama sürümleri bu sütunu sorguluyorsa hata verecektir.",
                    Recommendation = "Sütunu silmeden önce canlı uygulamanın bu sütunu okumayı bıraktığından (Contract aşaması) emin olun.",
                    LineNumber = drop.LineNumber,
                    SourceFile = drop.SourceFile
                });
            }
        }
    }

    private static AddColumnOperation? FindBestMatchingRenameCandidate(DropColumnOperation drop, List<AddColumnOperation> candidates)
    {
        if (string.IsNullOrEmpty(drop.ColumnName))
            return null;

        var dropClean = NormalizeColumnName(drop.ColumnName);
        AddColumnOperation? bestMatch = null;
        int bestScore = int.MaxValue;

        foreach (var add in candidates)
        {
            if (string.IsNullOrEmpty(add.ColumnName))
                continue;

            var addClean = NormalizeColumnName(add.ColumnName);
            bool isShort = dropClean.Length <= 3 || addClean.Length <= 3;

            int score = int.MaxValue;
            if (dropClean.Equals(addClean, StringComparison.OrdinalIgnoreCase))
            {
                score = 0;
            }
            else if (addClean.StartsWith(dropClean, StringComparison.OrdinalIgnoreCase) ||
                     dropClean.StartsWith(addClean, StringComparison.OrdinalIgnoreCase))
            {
                score = Math.Abs(addClean.Length - dropClean.Length);
            }
            else if (!isShort && ComputeLevenshteinDistance(dropClean, addClean) <= 2)
            {
                score = 10 + ComputeLevenshteinDistance(dropClean, addClean);
            }

            if (score < bestScore)
            {
                if (AreTypesBroadlyCompatible(drop.DataType, add.DataType))
                {
                    bestScore = score;
                    bestMatch = add;
                }
            }
        }

        return bestMatch;
    }

    private static string NormalizeColumnName(string name)
    {
        return name.Replace("_", "").Replace("-", "").Trim();
    }

    private static bool AreTypesBroadlyCompatible(string? type1, string? type2)
    {
        if (string.IsNullOrWhiteSpace(type1) || string.IsNullOrWhiteSpace(type2))
            return true; // Bilgi yoksa dışlama yapma

        var t1 = type1.ToLowerInvariant();
        var t2 = type2.ToLowerInvariant();

        var isString1 = t1.Contains("char") || t1.Contains("text") || t1.Contains("string");
        var isString2 = t2.Contains("char") || t2.Contains("text") || t2.Contains("string");
        if (isString1 && isString2) return true;

        var isInt1 = t1.Contains("int") || t1.Contains("byte") || t1.Contains("short") || t1.Contains("long");
        var isInt2 = t2.Contains("int") || t2.Contains("byte") || t2.Contains("short") || t2.Contains("long");
        if (isInt1 && isInt2) return true;

        return false;
    }

    private static int ComputeLevenshteinDistance(string s, string t)
    {
        if (string.IsNullOrEmpty(s)) return t.Length;
        if (string.IsNullOrEmpty(t)) return s.Length;

        var d = new int[s.Length + 1, t.Length + 1];
        for (var i = 0; i <= s.Length; i++) d[i, 0] = i;
        for (var j = 0; j <= t.Length; j++) d[0, j] = j;

        for (var j = 1; j <= t.Length; j++)
        {
            for (var i = 1; i <= s.Length; i++)
            {
                var cost = (s[i - 1] == t[j - 1]) ? 0 : 1;
                d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + cost);
            }
        }
        return d[s.Length, t.Length];
    }

    private enum BackfillStatus
    {
        None,
        SelectOrCommentOnly,
        PartialOrConditional,
        InvalidExecutionOrder,
        DestructiveSideEffect,
        ValidCandidateOrderVerified
    }

    private record BackfillAnalysisResult(BackfillStatus Status, string Detail, int SqlLineNumber);

    private static BackfillAnalysisResult AnalyzeSqlBackfill(
        string tableName,
        DropColumnOperation drop,
        AddColumnOperation add,
        List<RawSqlOperation> sqlOps,
        List<MigrationOperation>? scopeOps = null)
    {
        foreach (var sql in sqlOps)
        {
            var statements = SqlScriptLexer.SplitAndAnalyzeStatements(sql.SqlContent);
            if (statements.Count == 0)
                continue;

            // Eğer tüm ifadeler yorum veya boş ise
            if (statements.All(s => string.IsNullOrWhiteSpace(s.CleanSql)))
            {
                if (sql.SqlContent.Contains(tableName, StringComparison.OrdinalIgnoreCase) &&
                    sql.SqlContent.Contains(drop.ColumnName, StringComparison.OrdinalIgnoreCase))
                {
                    return new BackfillAnalysisResult(BackfillStatus.SelectOrCommentOnly, "SQL yalnızca yorum satırı içeriyor", sql.LineNumber);
                }
                continue;
            }

            // Eğer tek bir SELECT ifadesi ise
            if (statements.Count == 1 && statements[0].CommandKind == SqlCommandKind.Select)
            {
                var s = statements[0];
                if (s.CleanSql.Contains(tableName, StringComparison.OrdinalIgnoreCase) &&
                    s.CleanSql.Contains(drop.ColumnName, StringComparison.OrdinalIgnoreCase) &&
                    s.CleanSql.Contains(add.ColumnName, StringComparison.OrdinalIgnoreCase))
                {
                    return new BackfillAnalysisResult(BackfillStatus.SelectOrCommentOnly, "SQL bir SELECT komutudur; veri kopyalamaz", sql.LineNumber);
                }
                continue;
            }

            // Sütun ataması yapan UPDATE ifadesini ara
            var updateStmt = statements.FirstOrDefault(s => s.CommandKind == SqlCommandKind.Update &&
                (s.TargetTable?.Equals(tableName, StringComparison.OrdinalIgnoreCase) == true || s.CleanSql.Contains(tableName, StringComparison.OrdinalIgnoreCase)) &&
                s.HasAssignment(add.ColumnName, drop.ColumnName));

            if (updateStmt != null)
            {
                // Aynı SQL çağrısı içinde başka bir yıkıcı komut (örn: TRUNCATE, DROP) var mı?
                var destructive = statements.FirstOrDefault(s => s != updateStmt && s.IsDestructive);
                if (destructive != null)
                {
                    return new BackfillAnalysisResult(BackfillStatus.DestructiveSideEffect,
                        $"UPDATE veri taşıma komutu tespit edildi ancak aynı SQL çağrısı yıkıcı ek işlem barındırıyor: {destructive.DestructiveReason}", sql.LineNumber);
                }

                // Çalışma sırası denetimi (hem liste sırası hem satır numarası):
                int dropIdx = scopeOps != null ? scopeOps.IndexOf(drop) : -1;
                int addIdx = scopeOps != null ? scopeOps.IndexOf(add) : -1;
                int sqlIdx = scopeOps != null ? scopeOps.IndexOf(sql) : -1;

                bool isAfterDrop = (dropIdx >= 0 && sqlIdx > dropIdx) || (drop.LineNumber > 0 && sql.LineNumber > drop.LineNumber);
                bool isBeforeAdd = (addIdx >= 0 && sqlIdx < addIdx) || (add.LineNumber > 0 && sql.LineNumber < add.LineNumber);

                if (isAfterDrop)
                {
                    return new BackfillAnalysisResult(BackfillStatus.InvalidExecutionOrder, $"UPDATE komutu (Satır {sql.LineNumber}) DropColumn'dan (Satır {drop.LineNumber}) sonra yer alıyor", sql.LineNumber);
                }

                if (isBeforeAdd)
                {
                    return new BackfillAnalysisResult(BackfillStatus.InvalidExecutionOrder, $"UPDATE komutu (Satır {sql.LineNumber}) AddColumn'dan (Satır {add.LineNumber}) önce yer alıyor", sql.LineNumber);
                }

                // Kısmi / koşullu update kontrolü (WHERE id = 1 gibi sınırlı filtreler)
                if (updateStmt.HasWhereClause && !string.IsNullOrWhiteSpace(updateStmt.WhereCondition))
                {
                    var whereClause = updateStmt.WhereCondition.ToLowerInvariant();
                    var isGeneralNullFilter = whereClause.Contains("is null") || whereClause.Contains("is not null");
                    if (!isGeneralNullFilter && !whereClause.Equals("1=1") && !whereClause.Equals("true"))
                    {
                        return new BackfillAnalysisResult(BackfillStatus.PartialOrConditional, $"Koşullu WHERE filtresi tüm kayıtları taşımayabilir: 'WHERE {updateStmt.WhereCondition}'", sql.LineNumber);
                    }
                }

                return new BackfillAnalysisResult(BackfillStatus.ValidCandidateOrderVerified, "Yapısal UPDATE ve çalıştırma sırası doğrulandı", sql.LineNumber);
            }
        }

        return new BackfillAnalysisResult(BackfillStatus.None, "Veri taşıyan UPDATE komutu bulunamadı", 0);
    }

    private static void EvaluateAlterColumn(string tableName, AlterColumnOperation alter, MigrationSafetyReport report)
    {
        // 1. Nullability Daraltma Kontrolü
        if (alter.IsNullable == false)
        {
            if (alter.OldIsNullable == true)
            {
                report.Issues.Add(new MigrationRiskIssue
                {
                    RuleId = "MIG005",
                    Severity = RiskSeverity.High,
                    Detection = DetectionType.Confirmed,
                    Confidence = AnalysisConfidence.High,
                    TableName = tableName,
                    ColumnName = alter.ColumnName,
                    Title = $"Kısıtlama Daraltma (Confirmed): '{tableName}.{alter.ColumnName}' NOT NULL Yapılıyor (NULL ➡️ NOT NULL)",
                    Description = $"Önceden NULL alabilen '{alter.ColumnName}' sütununa kesin NOT NULL kısıtlaması getiriliyor.",
                    ImpactAnalysis = "Tabloda NULL değeri olan tek bir satır dahi varsa migration veritabanı hatasıyla başarısız olur.",
                    Recommendation = "Migration öncesinde tüm NULL kayıtları bir varsayılan değerle güncelleyen bir SQL backfill çalıştırın.",
                    LineNumber = alter.LineNumber,
                    SourceFile = alter.SourceFile
                });
            }
            else if (alter.OldIsNullable == null)
            {
                // Önceki durum migration'da scaffold edilmemiş (Inferred Risk)
                report.Issues.Add(new MigrationRiskIssue
                {
                    RuleId = "MIG005",
                    Severity = RiskSeverity.High,
                    Detection = DetectionType.Inferred,
                    Confidence = AnalysisConfidence.Medium,
                    TableName = tableName,
                    ColumnName = alter.ColumnName,
                    Title = $"Potansiyel Kısıtlama Daraltma (Inferred): '{tableName}.{alter.ColumnName}' NOT NULL (Önceki Durum Bilinmiyor)",
                    Description = $"'{tableName}.{alter.ColumnName}' sütunu NOT NULL yapılıyor ancak migration dosyasında önceki nullability (oldNullable) belirtilmemiş.",
                    ImpactAnalysis = "Eğer bu sütun önceden nullable idiyse ve tabloda NULL kayıtlar varsa migration çöker.",
                    Recommendation = "Önceki durumu model snapshot'tan veya şemadan doğrulayın; gerekirse NULL kayıtları önceden doldurun.",
                    LineNumber = alter.LineNumber,
                    SourceFile = alter.SourceFile
                });
            }
        }

        // 2. Veri Tipi Değişimi Kontrolü (Genişletme vs Daraltma)
        if (!string.IsNullOrEmpty(alter.DataType) && !string.IsNullOrEmpty(alter.OldDataType) &&
            !string.Equals(alter.DataType, alter.OldDataType, StringComparison.OrdinalIgnoreCase))
        {
            var classification = ClassifyTypeChange(alter.OldDataType, alter.DataType);

            switch (classification.Risk)
            {
                case TypeChangeRisk.PotentiallySafeWidening:
                    report.Issues.Add(new MigrationRiskIssue
                    {
                        RuleId = "MIG011_WIDENING",
                        Severity = RiskSeverity.Info,
                        Detection = DetectionType.Inferred,
                        Confidence = AnalysisConfidence.High,
                        TableName = tableName,
                        ColumnName = alter.ColumnName,
                        Title = $"Tip Genişletme (Safe Widening): '{alter.OldDataType}' ➡️ '{alter.DataType}'",
                        Description = $"'{tableName}.{alter.ColumnName}' kapasitesi artırılıyor. {classification.Explanation}",
                        ImpactAnalysis = "Kapasite artışı genellikle veri kaybına yol açmaz.",
                        Recommendation = "Genişletme güvenlidir; veritabanı disk alanı ve indeks boyutunu gözlemleyin.",
                        LineNumber = alter.LineNumber,
                        SourceFile = alter.SourceFile
                    });
                    break;

                case TypeChangeRisk.DestructiveNarrowing:
                    report.Issues.Add(new MigrationRiskIssue
                    {
                        RuleId = "MIG011",
                        Severity = RiskSeverity.High,
                        Detection = DetectionType.Confirmed,
                        Confidence = AnalysisConfidence.High,
                        TableName = tableName,
                        ColumnName = alter.ColumnName,
                        Title = $"Kritik Tip Daraltma (Narrowing): '{alter.OldDataType}' ➡️ '{alter.DataType}'",
                        Description = $"'{tableName}.{alter.ColumnName}' tipi daraltılıyor! {classification.Explanation}",
                        ImpactAnalysis = "Mevcut veriler yeni boyuta sığmazsa veri kaybı (truncation) veya migration çökmesi oluşur.",
                        Recommendation = "Mevcut tablodaki maksimum veri uzunluğunu kontrol edin; daraltmayı geri alın veya veriyi normalize edin.",
                        LineNumber = alter.LineNumber,
                        SourceFile = alter.SourceFile
                    });
                    break;

                case TypeChangeRisk.IncompatibleOrCrossCategory:
                default:
                    report.Issues.Add(new MigrationRiskIssue
                    {
                        RuleId = "MIG011",
                        Severity = RiskSeverity.High,
                        Detection = DetectionType.Inferred,
                        Confidence = AnalysisConfidence.Medium,
                        TableName = tableName,
                        ColumnName = alter.ColumnName,
                        Title = $"Çapraz Tip Değişimi: '{alter.OldDataType}' ➡️ '{alter.DataType}'",
                        Description = $"'{tableName}.{alter.ColumnName}' farklı bir veri tipine dönüştürülüyor. {classification.Explanation}",
                        ImpactAnalysis = "Veritabanı otomatik tür dönüşümü yapamazsa veri bozulması veya migration hatası oluşur.",
                        Recommendation = "Tip dönüşümünün güvenli olduğunu ve veri kaybına yol açmadığını test ediniz.",
                        LineNumber = alter.LineNumber,
                        SourceFile = alter.SourceFile
                    });
                    break;
            }
        }
    }

    private enum TypeChangeRisk
    {
        PotentiallySafeWidening,
        DestructiveNarrowing,
        IncompatibleOrCrossCategory,
        Uncertain
    }

    private record TypeClassificationResult(TypeChangeRisk Risk, string Explanation);

    private static TypeClassificationResult ClassifyTypeChange(string oldType, string newType)
    {
        var oldClean = oldType.Trim().ToLowerInvariant();
        var newClean = newType.Trim().ToLowerInvariant();

        // 1. String uzunluk karşılaştırması: varchar(n), nvarchar(n)
        var stringPattern = @"^(n?varchar|char|character varying)\((\d+|max)\)$";
        var oldMatch = Regex.Match(oldClean, stringPattern);
        var newMatch = Regex.Match(newClean, stringPattern);

        if (oldMatch.Success && newMatch.Success)
        {
            var oldBase = oldMatch.Groups[1].Value;
            var newBase = newMatch.Groups[1].Value;
            var oldLenStr = oldMatch.Groups[2].Value;
            var newLenStr = newMatch.Groups[2].Value;

            if (oldBase.Equals(newBase, StringComparison.OrdinalIgnoreCase) ||
                (oldBase.Contains("char") && newBase.Contains("char")))
            {
                if (newLenStr.Equals("max", StringComparison.OrdinalIgnoreCase))
                {
                    return new TypeClassificationResult(TypeChangeRisk.PotentiallySafeWidening, $"Maksimum sınırsız uzunluğa ({newClean}) genişletiliyor.");
                }

                if (oldLenStr.Equals("max", StringComparison.OrdinalIgnoreCase))
                {
                    return new TypeClassificationResult(TypeChangeRisk.DestructiveNarrowing, $"Sınırsız uzunluktan sınırlı uzunluğa ({newLenStr}) daraltılıyor.");
                }

                if (int.TryParse(oldLenStr, out var oldLen) && int.TryParse(newLenStr, out var newLen))
                {
                    if (newLen > oldLen)
                    {
                        return new TypeClassificationResult(TypeChangeRisk.PotentiallySafeWidening, $"{oldLen} karakterden {newLen} karaktere genişletiliyor.");
                    }
                    if (newLen < oldLen)
                    {
                        return new TypeClassificationResult(TypeChangeRisk.DestructiveNarrowing, $"{oldLen} karakterden {newLen} karaktere daraltılıyor.");
                    }
                }
            }
        }

        // 2. Tamsayı genişletme: smallint -> int -> bigint
        var intRank = new Dictionary<string, int>
        {
            { "tinyint", 1 },
            { "smallint", 2 },
            { "int", 3 },
            { "integer", 3 },
            { "bigint", 4 },
            { "long", 4 }
        };

        if (intRank.TryGetValue(oldClean, out var oldRank) && intRank.TryGetValue(newClean, out var newRank))
        {
            if (newRank > oldRank)
            {
                return new TypeClassificationResult(TypeChangeRisk.PotentiallySafeWidening, $"Tamsayı aralığı ({oldClean} -> {newClean}) genişletiliyor.");
            }
            if (newRank < oldRank)
            {
                return new TypeClassificationResult(TypeChangeRisk.DestructiveNarrowing, $"Tamsayı aralığı ({oldClean} -> {newClean}) daraltılıyor.");
            }
        }

        return new TypeClassificationResult(TypeChangeRisk.IncompatibleOrCrossCategory, "Tipler farklı kategorilerde veya özel sağlayıcı türündedir.");
    }
}
