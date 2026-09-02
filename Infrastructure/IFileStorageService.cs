using Microsoft.AspNetCore.Http;

namespace ArnoxPay.Infrastructure;

public interface IFileStorageService
{
    Task<string> SaveAccommodationImageAsync(IFormFile imageFile);
    void DeleteFile(string relativePath);
}
