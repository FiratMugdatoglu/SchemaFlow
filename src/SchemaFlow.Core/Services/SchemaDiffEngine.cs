using SchemaFlow.Core.Models;

namespace SchemaFlow.Core.Services;

/// <summary>
/// İki veritabanı şemasını (eski/base ve yeni/target) karşılaştırarak
/// aralarındaki farkları ve canlı sistemi bozabilecek (Breaking Changes) riskleri tespit eder.
/// </summary>
public class SchemaDiffEngine
{
    public SchemaDiffResult Compare(DatabaseSchema baseSchema, DatabaseSchema targetSchema)
    {
        var result = new SchemaDiffResult();

        // 1. Silinen Tabloları Bul (Base'de var, Target'ta yok -> 🔴 KRİTİK BREAKING CHANGE)
        foreach (var baseTable in baseSchema.Tables)
        {
            var targetTable = targetSchema.FindTable(baseTable.Name);
            if (targetTable == null)
            {
                result.Changes.Add(new SchemaChange
                {
                    Type = ChangeType.TableDropped,
                    TableName = baseTable.Name,
                    Description = $"'{baseTable.Name}' tablosu tamamen silindi!",
                    IsBreaking = true,
                    OldValue = baseTable.Name
                });
            }
        }

        // 2. Eklenen ve Değişen Tabloları Bul
        foreach (var targetTable in targetSchema.Tables)
        {
            var baseTable = baseSchema.FindTable(targetTable.Name);

            // A) Yeni Eklenen Tablo (🟢 GÜVENLİ)
            if (baseTable == null)
            {
                result.Changes.Add(new SchemaChange
                {
                    Type = ChangeType.TableAdded,
                    TableName = targetTable.Name,
                    Description = $"'{targetTable.Name}' tablosu ({targetTable.Columns.Count} sütun) sisteme yeni eklendi.",
                    IsBreaking = false,
                    NewValue = targetTable.Name
                });
                continue;
            }

            // B) Ortak Tabloların Sütunlarını Karşılaştır
            CompareTableColumns(baseTable, targetTable, result);

            // C) Yabancı Anahtar (FK) Değişikliklerini Karşılaştır
            CompareForeignKeys(baseTable, targetTable, result);
        }

        return result;
    }

    private static void CompareTableColumns(Table baseTable, Table targetTable, SchemaDiffResult result)
    {
        // 1. Silinen Sütunlar (🔴 KRİTİK BREAKING CHANGE)
        foreach (var baseCol in baseTable.Columns)
        {
            var targetCol = targetTable.Columns.FirstOrDefault(c => string.Equals(c.Name, baseCol.Name, StringComparison.OrdinalIgnoreCase));
            if (targetCol == null)
            {
                result.Changes.Add(new SchemaChange
                {
                    Type = ChangeType.ColumnDropped,
                    TableName = baseTable.Name,
                    ColumnName = baseCol.Name,
                    Description = $"'{baseTable.Name}' tablosundan '{baseCol.Name}' ({baseCol.DataType}) sütunu silindi!",
                    IsBreaking = true,
                    OldValue = $"{baseCol.Name} {baseCol.DataType}"
                });
            }
        }

        // 2. Eklenen ve Değiştirilen Sütunlar
        foreach (var targetCol in targetTable.Columns)
        {
            var baseCol = baseTable.Columns.FirstOrDefault(c => string.Equals(c.Name, targetCol.Name, StringComparison.OrdinalIgnoreCase));

            // Yeni Eklenen Sütun
            if (baseCol == null)
            {
                // Eğer eklenen sütun NOT NULL ise ve varsayılan (DEFAULT) değeri yoksa, mevcut tablodaki satırlar için patlar! (🔴 BREAKING)
                var isBreaking = !targetCol.IsNullable && string.IsNullOrEmpty(targetCol.DefaultValue) && !targetCol.IsIdentity;
                var desc = isBreaking
                    ? $"'{targetTable.Name}' tablosuna varsayılan değeri olmayan 'NOT NULL {targetCol.Name}' sütunu eklendi! (Mevcut kayıtlar varsa hata verir)"
                    : $"'{targetTable.Name}' tablosuna '{targetCol.Name}' ({targetCol.DataType}) sütunu eklendi.";

                result.Changes.Add(new SchemaChange
                {
                    Type = ChangeType.ColumnAdded,
                    TableName = targetTable.Name,
                    ColumnName = targetCol.Name,
                    Description = desc,
                    IsBreaking = isBreaking,
                    NewValue = $"{targetCol.Name} {targetCol.DataType}"
                });
                continue;
            }

            // Veri Tipi Değişmiş mi? (🔴 BREAKING)
            if (!string.Equals(baseCol.DataType, targetCol.DataType, StringComparison.OrdinalIgnoreCase))
            {
                result.Changes.Add(new SchemaChange
                {
                    Type = ChangeType.ColumnTypeChanged,
                    TableName = targetTable.Name,
                    ColumnName = targetCol.Name,
                    Description = $"'{targetTable.Name}.{targetCol.Name}' veri tipi değiştirildi: '{baseCol.DataType}' ➡️ '{targetCol.DataType}'",
                    IsBreaking = true,
                    OldValue = baseCol.DataType,
                    NewValue = targetCol.DataType
                });
            }

            // Nullable durumdan NOT NULL durumuna çekilmiş mi? (🔴 BREAKING)
            if (baseCol.IsNullable && !targetCol.IsNullable)
            {
                result.Changes.Add(new SchemaChange
                {
                    Type = ChangeType.ColumnNullabilityChanged,
                    TableName = targetTable.Name,
                    ColumnName = targetCol.Name,
                    Description = $"'{targetTable.Name}.{targetCol.Name}' sütununa NOT NULL kısıtlaması getirildi! (Mevcut NULL kayıtlar patlayabilir)",
                    IsBreaking = true,
                    OldValue = "Nullable: true",
                    NewValue = "Nullable: false"
                });
            }

            // Birincil Anahtar (PK) durumu değişmiş mi? (🔴 BREAKING)
            if (baseCol.IsPrimaryKey != targetCol.IsPrimaryKey)
            {
                result.Changes.Add(new SchemaChange
                {
                    Type = ChangeType.PrimaryKeyChanged,
                    TableName = targetTable.Name,
                    ColumnName = targetCol.Name,
                    Description = $"'{targetTable.Name}.{targetCol.Name}' birincil anahtar (Primary Key) durumu değiştirildi!",
                    IsBreaking = true,
                    OldValue = $"PK: {baseCol.IsPrimaryKey}",
                    NewValue = $"PK: {targetCol.IsPrimaryKey}"
                });
            }
        }
    }

    private static void CompareForeignKeys(Table baseTable, Table targetTable, SchemaDiffResult result)
    {
        // Eklenen Yeni İlişkiler (🟢 GÜVENLİ)
        foreach (var targetFk in targetTable.ForeignKeys)
        {
            var existsInBase = baseTable.ForeignKeys.Any(f =>
                string.Equals(f.TargetTable, targetFk.TargetTable, StringComparison.OrdinalIgnoreCase) &&
                f.SourceColumns.SequenceEqual(targetFk.SourceColumns, StringComparer.OrdinalIgnoreCase));

            if (!existsInBase)
            {
                result.Changes.Add(new SchemaChange
                {
                    Type = ChangeType.ForeignKeyAdded,
                    TableName = targetTable.Name,
                    Description = $"'{targetTable.Name}' ➡️ '{targetFk.TargetTable}' tablosuna yeni yabancı anahtar (FK) ilişkisi bağlandı.",
                    IsBreaking = false,
                    NewValue = $"{targetTable.Name}({string.Join(",", targetFk.SourceColumns)}) -> {targetFk.TargetTable}"
                });
            }
        }
    }
}
