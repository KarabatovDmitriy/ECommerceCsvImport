using System.IO;
using ECommerceCsvImport.Models;
using ECommerceCsvImport.Services;
using Microsoft.AspNetCore.Mvc;

namespace ECommerceCsvImport.Controllers;

public class ProductsController : Controller
{
    private readonly IProductRepository _repository;
    private readonly IProductImportService _importService;
    private readonly IWebHostEnvironment _environment;

    public ProductsController(
        IProductRepository repository,
        IProductImportService importService,
        IWebHostEnvironment environment)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _importService = importService ?? throw new ArgumentNullException(nameof(importService));
        _environment = environment ?? throw new ArgumentNullException(nameof(environment));
    }

    [HttpGet]
    public IActionResult Index()
    {
        return View(new ProductsIndexViewModel { Products = _repository.GetAll() });
    }

    [HttpGet]
    public IActionResult Import()
    {
        return View(new ImportViewModel());
    }

    [HttpPost]
    public IActionResult Import([FromForm] string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return View("Import", new ImportViewModel
            {
                FilePath = string.Empty,
                Result = new ImportResult
                {
                    Errors = { new ImportError { RowNumber = 0, Message = "Укажите путь к CSV-файлу" } }
                }
            });
        }

        string resolved = ResolveFilePath(filePath);
        var result = _importService.ImportFromCsv(resolved);

        return View("Import", new ImportViewModel { FilePath = filePath, Result = result });
    }

    private string ResolveFilePath(string filePath)
    {
        if (Path.IsPathRooted(filePath) && System.IO.File.Exists(filePath)) return filePath;

        var relative = filePath.TrimStart('/', '\\');
        var webRootPath = Path.Combine(_environment.WebRootPath, relative.Replace('/', Path.DirectorySeparatorChar));
        if (System.IO.File.Exists(webRootPath)) return webRootPath;

        var contentRootPath = Path.Combine(_environment.ContentRootPath, relative.Replace('/', Path.DirectorySeparatorChar));
        if (System.IO.File.Exists(contentRootPath)) return contentRootPath;

        return filePath;
    }
}