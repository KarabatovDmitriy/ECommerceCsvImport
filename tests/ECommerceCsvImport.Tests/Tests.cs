using ECommerceCsvImport.Models;
using ECommerceCsvImport.Services;

namespace ECommerceCsvImport.Tests;

public class Tests
{
    // --- Тесты репозитория ---
    [Fact]
    public void Repository_AddAndGet_Works()
    {
        var repo = new InMemoryProductRepository();
        repo.Add(new Product { Sku = "1001", Name = "Test" });
        Assert.Equal(1, repo.Count);
        Assert.NotNull(repo.GetBySku("1001"));
    }

    // --- Тесты парсера ---
    [Fact]
    public void Parser_HandlesQuotes_Works()
    {
        var line = "1012,\"Ноутбук, игровой\",Desc,59990,RUB,Comp,1,/img.jpg,true,ASUS,36,VivoBook";
        var result = CsvLineParser.Parse(line);
        Assert.Equal(12, result.Count);
        Assert.Equal("Ноутбук, игровой", result[1]);
    }

    // --- Тесты сервиса импорта ---
    [Fact]
    public void Import_ValidData_Works()
    {
        var repo = new InMemoryProductRepository();
        var service = new ProductImportService(repo);
        var lines = new[] {
            "sku,name,description,price,currency,category_name,stock,image_url,is_active,brand,warranty_months,model_number",
            "1001,Ноутбук Lenovo,Desc,45990,RUB,Компьютеры,5,/img.jpg,true,Lenovo,24,IdeaPad"
        };
        var result = service.ImportFromLines(lines);
        
        Assert.Equal(1, result.TotalRows);
        Assert.Equal(1, result.ImportedCount);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Import_InvalidPrice_ReturnsError()
    {
        var repo = new InMemoryProductRepository();
        var service = new ProductImportService(repo);
        var lines = new[] {
            "sku,name,description,price,currency,category_name,stock,image_url,is_active,brand,warranty_months,model_number",
            "1003,Клавиатура,Desc,-500,RUB,Периферия,7,/img.jpg,true,A4Tech,24,V3M"
        };
        var result = service.ImportFromLines(lines);
        
        Assert.Equal(0, result.ImportedCount);
        Assert.Contains(result.Errors, e => e.Message.Contains("price"));
    }
}