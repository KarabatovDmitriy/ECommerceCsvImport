using System.Globalization;
using ECommerceCsvImport.Models;

namespace ECommerceCsvImport.Services;

public class ProductImportService : IProductImportService
{
    private readonly IProductRepository _repository;

    private static readonly string[] RequiredHeaders =
    {
        "sku", "name", "description", "price", "currency", "category_name",
        "stock", "image_url", "is_active", "brand", "warranty_months", "model_number"
    };

    public ProductImportService(IProductRepository repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    public ImportResult ImportFromCsv(string filePath)
    {
        var result = new ImportResult();

        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            result.Errors.Add(new ImportError { RowNumber = 0, Message = "Файл не найден" });
            return result;
        }

        try
        {
            var lines = File.ReadAllLines(filePath);
            return ImportFromLines(lines);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            result.Errors.Add(new ImportError { RowNumber = 0, Message = $"Ошибка чтения файла: {ex.Message}" });
            return result;
        }
    }

    public ImportResult ImportFromLines(IEnumerable<string> lines)
    {
        var result = new ImportResult();
        var lineList = lines?.ToList() ?? new List<string>();

        int headerIndex = -1;
        for (int i = 0; i < lineList.Count; i++)
        {
            if (!string.IsNullOrWhiteSpace(lineList[i])) { headerIndex = i; break; }
        }

        if (headerIndex < 0)
        {
            result.Errors.Add(new ImportError { RowNumber = 0, Message = "Данные для импорта отсутствуют" });
            return result;
        }

        var headerValues = CsvLineParser.Parse(lineList[headerIndex]);
        var columnMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < headerValues.Count; i++)
        {
            var name = headerValues[i].Trim();
            if (!columnMap.ContainsKey(name)) columnMap[name] = i;
        }

        var missing = RequiredHeaders.Where(h => !columnMap.ContainsKey(h)).ToList();
        if (missing.Count > 0)
        {
            result.Errors.Add(new ImportError
            {
                RowNumber = headerIndex + 1,
                Message = $"Неверные заголовки CSV. Отсутствуют: {string.Join(", ", missing)}"
            });
            return result;
        }

        var importedSkus = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int expectedCount = headerValues.Count;

        for (int i = headerIndex + 1; i < lineList.Count; i++)
        {
            var line = lineList[i];
            if (string.IsNullOrWhiteSpace(line)) continue;

            result.TotalRows++;
            int rowNumber = i + 1;

            var values = CsvLineParser.Parse(line);

            if (values.Count != expectedCount)
            {
                result.Errors.Add(new ImportError { RowNumber = rowNumber, Message = "Неверное количество полей в строке" });
                continue;
            }

            string GetValue(string col) =>
                columnMap.TryGetValue(col, out int idx) && idx < values.Count ? values[idx].Trim() : string.Empty;

            var rowErrors = new List<ImportError>();

            string sku = GetValue("sku");
            if (string.IsNullOrWhiteSpace(sku))
                rowErrors.Add(new ImportError { RowNumber = rowNumber, Message = "Поле sku не может быть пустым" });

            string name = GetValue("name");
            if (string.IsNullOrWhiteSpace(name))
                rowErrors.Add(new ImportError { RowNumber = rowNumber, Message = "Поле name не может быть пустым" });

            decimal price = 0;
            bool priceValid = decimal.TryParse(GetValue("price"), NumberStyles.Any, CultureInfo.InvariantCulture, out price) && price > 0;
            if (!priceValid)
                rowErrors.Add(new ImportError { RowNumber = rowNumber, Message = "Поле price должно быть числом больше 0" });

            int stock = 0;
            bool stockValid = int.TryParse(GetValue("stock"), out stock) && stock >= 0;
            if (!stockValid)
                rowErrors.Add(new ImportError { RowNumber = rowNumber, Message = "Остаток на складе должен быть целым числом не меньше 0" });

            bool isActive = false;
            bool activeValid = TryParseBool(GetValue("is_active"), out isActive);
            if (!activeValid)
                rowErrors.Add(new ImportError { RowNumber = rowNumber, Message = "Поле is_active должно иметь значение true, false, 1 или 0" });

            string brand = GetValue("brand");
            if (string.IsNullOrWhiteSpace(brand))
                rowErrors.Add(new ImportError { RowNumber = rowNumber, Message = "Поле brand обязательно для варианта «Электроника»" });

            int warranty = 0;
            bool warrantyValid = int.TryParse(GetValue("warranty_months"), out warranty) && warranty >= 0 && warranty <= 60;
            if (!warrantyValid)
                rowErrors.Add(new ImportError { RowNumber = rowNumber, Message = "Гарантия должна быть целым числом от 0 до 60 месяцев" });

            if (rowErrors.Count > 0)
            {
                result.Errors.AddRange(rowErrors);
                continue;
            }

            bool isDuplicate = _repository.GetBySku(sku) != null || importedSkus.Contains(sku);
            if (isDuplicate)
            {
                result.Errors.Add(new ImportError { RowNumber = rowNumber, Message = "Артикул товара уже существует" });
                continue;
            }

            var product = new Product
            {
                Sku = sku,
                Name = name,
                Description = GetValue("description"),
                Price = price,
                Currency = NormalizeCurrency(GetValue("currency")),
                CategoryName = NormalizeCategory(GetValue("category_name")),
                StockQuantity = stock,
                ImageUrl = GetValue("image_url"),
                IsActive = isActive,
                Brand = brand,
                WarrantyMonths = warranty,
                ModelNumber = GetValue("model_number")
            };

            _repository.Add(product);
            importedSkus.Add(sku);
            result.ImportedCount++;
        }

        if (result.TotalRows == 0 && result.Errors.Count == 0)
        {
            result.Errors.Add(new ImportError { RowNumber = 0, Message = "Данные для импорта отсутствуют" });
        }

        return result;
    }

    private static bool TryParseBool(string value, out bool result)
    {
        result = false;
        if (string.IsNullOrWhiteSpace(value)) return false;
        var v = value.Trim();
        if (v.Equals("true", StringComparison.OrdinalIgnoreCase) || v == "1") { result = true; return true; }
        if (v.Equals("false", StringComparison.OrdinalIgnoreCase) || v == "0") { result = false; return true; }
        return false;
    }

    private static string NormalizeCurrency(string value) =>
        string.IsNullOrWhiteSpace(value) ? "RUB" : value.Trim().ToUpperInvariant();

    private static string NormalizeCategory(string value) =>
        string.IsNullOrWhiteSpace(value) ? "Без категории" : value.Trim();
}