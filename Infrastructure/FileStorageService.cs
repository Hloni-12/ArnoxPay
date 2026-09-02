using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;

namespace ArnoxPay.Infrastructure;

public class FileStorageService : IFileStorageService
{
    private static readonly string[] AllowedExtensions =
        [".jpg", ".jpeg", ".png", ".webp"];

    private readonly IWebHostEnvironment _environment;

    public FileStorageService(IWebHostEnvironment environment)
    {
        _environment = environment;
    }

    public async Task<string> SaveAccommodationImageAsync(IFormFile imageFile)
    {
        if (imageFile == null || imageFile.Length == 0)
            throw new InvalidOperationException("Please select an image.");

        if (imageFile.Length > 5 * 1024 * 1024)
            throw new InvalidOperationException("Select an image smaller than 5 MB.");

        var extension = Path.GetExtension(imageFile.FileName).ToLowerInvariant();

        if (!AllowedExtensions.Contains(extension))
        {
            throw new InvalidOperationException(
                "Only JPG, JPEG, PNG, and WEBP images are accepted.");
        }

        var root = _environment.WebRootPath
            ?? Path.Combine(_environment.ContentRootPath, "wwwroot");

        var directory = Path.Combine(root, "uploads", "accomodations");
        Directory.CreateDirectory(directory);

        var fileName = $"{Guid.NewGuid():N}{extension}";
        var fullPath = Path.Combine(directory, fileName);

        await using var stream = File.Create(fullPath);
        await imageFile.CopyToAsync(stream);

        return $"/uploads/accomodations/{fileName}";
    }

    public void DeleteFile(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
            return;

        var root = _environment.WebRootPath
            ?? Path.Combine(_environment.ContentRootPath, "wwwroot");

        var cleanPath = relativePath
            .TrimStart('/', '\\')
            .Replace('/', Path.DirectorySeparatorChar)
            .Replace('\\', Path.DirectorySeparatorChar);

        var fullPath = Path.Combine(root, cleanPath);

        if (File.Exists(fullPath))
            File.Delete(fullPath);
    }
}
