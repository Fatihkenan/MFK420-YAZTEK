using YAZTEK.API.Entities;
using YAZTEK.API.Repositories;

namespace YAZTEK.API.Services;

public class StudentService : IStudentService
{
    private readonly IStudentRepository _repository;

    // Dependency Injection: Service, Repository'i dışarıdan istiyor
    public StudentService(IStudentRepository repository)
    {
        _repository = repository;
    }

    public async Task<IEnumerable<Student>> GetAllStudentsAsync()
    {
        return await _repository.GetAllAsync();
    }

    public async Task<Student?> GetStudentByIdAsync(int id)
    {
        return await _repository.GetByIdAsync(id);
    }

    public async Task AddStudentAsync(Student student)
    {
        // İş kuralı: Öğrenci eklenirken oluşturulma tarihini otomatik olarak şu anki zaman yap
        student.CreatedAt = DateTime.UtcNow;

        await _repository.AddAsync(student);
        await _repository.SaveChangesAsync(); // Değişiklikleri veritabanına kaydet
    }
}