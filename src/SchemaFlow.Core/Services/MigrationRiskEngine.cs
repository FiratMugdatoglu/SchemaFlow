using SchemaFlow.Core.Models;
using SchemaFlow.Core.Models.Operations;

namespace SchemaFlow.Core.Services;

/// <summary>
/// Normalize edilmiş MigrationOperation modelleri üzerinde offline statik güvenlik ve risk analizini yürüten motordur.
/// Yanlış pozitifleri azaltmak için kesin tespit (Confirmed) ile olasılık çıkarımını (Inferred) ayırır.
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

        // Yalnızca Up metodundaki operasyonları risk analizine tabi tutuyoruz
        // (Down rollback içindir, asıl deployment riski Up içerisindedir)
        var upOps = opList.Where(o => o.Scope != MigrationMethodScope.Down).ToList();

        // 1. Tablo bazlı gruplandırma ve kontroller
        var tableGroups = upOps.Where(o => !string.IsNullOrEmpty(o.TableName))
                               .GroupBy(o => o.TableName, StringComparer.OrdinalIgnoreCase);

        // Raw SQL operasyonlarını tara (Veri taşıma tespiti için)
        var rawSqls = upOps.OfType<RawSqlOperation>().ToList();

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

            // C) Expand & Contract ve Sütun Silme/Ekleme Analizi
            var droppedCols = tableOps.OfType<DropColumnOperation>().ToList();
            var addedCols = tableOps.OfType<AddColumnOperation>().ToList();

            if (droppedCols.Count > 0 && addedCols.Count > 0)
            {
                // Aynı tabloda DropColumn ve AddColumn birlikte varsa: Klasik C# Property Rename tuzağı!
                foreach (var drop in droppedCols)
                {
                    var added = addedCols.First();

                    // Arada veri taşıyan SQL var mı kontrol et
                    var hasDataMigrationSql = rawSqls.Any(s =>
                        s.SqlContent.Contains(tableName, StringComparison.OrdinalIgnoreCase) &&
                        s.SqlContent.Contains(drop.ColumnName, StringComparison.OrdinalIgnoreCase) &&
                        s.SqlContent.Contains(added.ColumnName, StringComparison.OrdinalIgnoreCase));

                    if (!hasDataMigrationSql)
                    {
                        report.Issues.Add(new MigrationRiskIssue
                        {
                            RuleId = "MIG003",
                            Severity = RiskSeverity.Critical,
                            Detection = DetectionType.Inferred,
                            Confidence = AnalysisConfidence.High,
                            TableName = tableName,
                            ColumnName = drop.ColumnName,
                            Title = $"🔴 Potansiyel Veri Kaybı (Expand & Contract İhlali): '{drop.ColumnName}' ➡️ '{added.ColumnName}'",
                            Description = $"'{tableName}' tablosunda '{drop.ColumnName}' silinirken aynı anda '{added.ColumnName}' ekleniyor. EF Core'da bir C# property'si yeniden adlandırıldığında varsayılan olarak bu üretilir ve canlıda VERİ KAYBINA yol açar!",
                            ImpactAnalysis = $"'{drop.ColumnName}' sütunundaki mevcut tüm veriler silinir. Arada veri taşıma (UPDATE backfill) operasyonu tespit edilemedi.",
                            Recommendation = "Veri kaybını önlemek için Expand & Contract desenini uygulayın veya RenameColumn metodunu kullanın.",
                            LineNumber = drop.LineNumber,
                            SourceFile = drop.SourceFile
                        });

                        report.ExpandContractPlans.Add(new ExpandContractPlan
                        {
                            TableName = tableName,
                            DroppedColumn = drop.ColumnName,
                            AddedColumn = added.ColumnName,
                            Reason = $"'{tableName}' tablosunda '{drop.ColumnName}' silinip yerine '{added.ColumnName}' eklenmiş. Arada verileri kopyalayan SQL bulunamadığı için canlı ortamda mevcut veriler silinecektir.",
                            PhaseSteps = new List<string>
                            {
                                $"Aşama 1 (Expand): '{added.ColumnName}' sütununu nullable veya güvenli varsayılan değer ile canlıya ekleyin.",
                                $"Aşama 2 (Backfill): Verileri kopyalayın: UPDATE [{tableName}] SET [{added.ColumnName}] = [{drop.ColumnName}] WHERE [{added.ColumnName}] IS NULL;",
                                $"Aşama 3 (Migrate): Uygulamanızı yeni '{added.ColumnName}' sütununu kullanacak şekilde canlıya deploy edin.",
                                $"Aşama 4 (Contract): Bir sonraki sürümde eski '{drop.ColumnName}' sütununu güvenle Drop edin.",
                                $"Hızlı Alternatif: Eğer kesintili bakım penceresi varsa 'migrationBuilder.RenameColumn(name: \"{drop.ColumnName}\", newName: \"{added.ColumnName}\", table: \"{tableName}\");' kullanabilirsiniz."
                            },
                            SuggestedSqlBackfill = $"UPDATE [{tableName}] SET [{added.ColumnName}] = [{drop.ColumnName}] WHERE [{added.ColumnName}] IS NULL;",
                            SuggestedEfCoreCode = $"migrationBuilder.RenameColumn(name: \"{drop.ColumnName}\", newName: \"{added.ColumnName}\", table: \"{tableName}\");"
                        });
                    }
                    else
                    {
                        report.Issues.Add(new MigrationRiskIssue
                        {
                            RuleId = "MIG003",
                            Severity = RiskSeverity.Medium,
                            Detection = DetectionType.Inferred,
                            Confidence = AnalysisConfidence.Medium,
                            TableName = tableName,
                            ColumnName = drop.ColumnName,
                            Title = $"Bilgi: '{tableName}' Tablosunda Veri Taşımalı Sütun Değişimi",
                            Description = $"'{drop.ColumnName}' silinip '{added.ColumnName}' eklenmiş, ancak arada veri taşıyan SQL komutu tespit edildi.",
                            ImpactAnalysis = "SQL komutunun tüm satırları başarıyla kopyaladığını doğrulayın.",
                            Recommendation = "Veri transferinin idempotent olduğunu ve NULL kayıtları doğru işlediğini test edin.",
                            LineNumber = drop.LineNumber,
                            SourceFile = drop.SourceFile
                        });
                    }
                }
            }
            else
            {
                // Tekil Sütun Silinmesi (DropColumn)
                foreach (var drop in droppedCols)
                {
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

            // D) Güvensiz NOT NULL Sütun Eklenmesi
            foreach (var add in addedCols)
            {
                if (!add.IsNullable && string.IsNullOrEmpty(add.DefaultValue) && string.IsNullOrEmpty(add.DefaultValueSql))
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
            }

            // E) AlterColumn Kontrolleri
            foreach (var alter in tableOps.OfType<AlterColumnOperation>())
            {
                // Nullable -> NOT NULL geçişi
                if (alter.IsNullable == false && alter.OldIsNullable == true)
                {
                    report.Issues.Add(new MigrationRiskIssue
                    {
                        RuleId = "MIG005",
                        Severity = RiskSeverity.High,
                        Detection = alter.Detection,
                        Confidence = alter.Confidence,
                        TableName = tableName,
                        ColumnName = alter.ColumnName,
                        Title = $"Kısıtlama Daraltma: '{tableName}.{alter.ColumnName}' NOT NULL Yapılıyor",
                        Description = $"Önceden NULL alabilen '{alter.ColumnName}' sütununa NOT NULL kısıtlaması getiriliyor.",
                        ImpactAnalysis = "Tabloda NULL değeri olan tek bir satır dahi varsa migration başarısız olur.",
                        Recommendation = "Migration öncesinde tüm NULL kayıtları bir varsayılan değerle güncelleyen bir SQL backfill çalıştırın.",
                        LineNumber = alter.LineNumber,
                        SourceFile = alter.SourceFile
                    });
                }

                // Veri tipi daralması (Örn: string uzunluğu daraltılması veya int -> short gibi)
                if (!string.IsNullOrEmpty(alter.DataType) && !string.IsNullOrEmpty(alter.OldDataType) &&
                    !string.Equals(alter.DataType, alter.OldDataType, StringComparison.OrdinalIgnoreCase))
                {
                    report.Issues.Add(new MigrationRiskIssue
                    {
                        RuleId = "MIG011",
                        Severity = RiskSeverity.High,
                        Detection = DetectionType.Inferred,
                        Confidence = AnalysisConfidence.Medium,
                        TableName = tableName,
                        ColumnName = alter.ColumnName,
                        Title = $"Veri Tipi Değişimi: '{alter.OldDataType}' ➡️ '{alter.DataType}'",
                        Description = $"'{tableName}.{alter.ColumnName}' sütununun tipi değiştiriliyor.",
                        ImpactAnalysis = "Eğer mevcut veriler yeni tipe sığmazsa veya otomatik cast edilemezse veri kaybı (truncation) veya migration hatası oluşur.",
                        Recommendation = "Tip dönüşümünün mevcut veriler üzerinde veri kaybına yol açmadığından emin olun.",
                        LineNumber = alter.LineNumber,
                        SourceFile = alter.SourceFile
                    });
                }
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

        // 2. Ham SQL Operasyonları Kontrolü
        foreach (var sqlOp in rawSqls)
        {
            report.Issues.Add(new MigrationRiskIssue
            {
                RuleId = "MIG010",
                Severity = RiskSeverity.Medium,
                Detection = DetectionType.Confirmed,
                Confidence = AnalysisConfidence.High,
                TableName = "RawSQL",
                Title = $"Doğrudan Ham SQL Çalıştırılıyor (Satır {sqlOp.LineNumber})",
                Description = $"Migration içerisinde 'migrationBuilder.Sql(...)' çağrısı tespit edildi ({sqlOp.SqlContent.Length} karakter).",
                ImpactAnalysis = "Çevrimdışı statik analiz keyfi SQL komutlarının veri güvenliğini garanti edemez.",
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
}
