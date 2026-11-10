using YAZTEK.API.DTOs;
using YAZTEK.API.Entities;
using YAZTEK.API.Repositories;

namespace YAZTEK.API.Services;

public class StudentService : IStudentService
{
    private readonly IStudentRepository _repository;

    public StudentService(IStudentRepository repository)
    {
        _repository = repository;
    }

    public async Task<IEnumerable<StudentDto>> GetAllStudentsAsync()
    {
        var students = await _repository.GetAllAsync();

        return students.Select(s => new StudentDto
        {
            Id = s.Id,
            FirstName = s.FirstName,
            LastName = s.LastName,
            StudentNumber = s.StudentNumber,
            GradeLevel = s.GradeLevel,
            CreatedAt = s.CreatedAt
        });
    }

    public async Task<StudentDto?> GetStudentByIdAsync(int id)
    {
        var student = await _repository.GetByIdAsync(id);
        if (student == null) return null;

        return new StudentDto
        {
            Id = student.Id,
            FirstName = student.FirstName,
            LastName = student.LastName,
            StudentNumber = student.StudentNumber,
            GradeLevel = student.GradeLevel,
            CreatedAt = student.CreatedAt
        };
    }

    public async Task<StudentDto> AddStudentAsync(CreateStudentDto createStudentDto)
    {
        var student = new Student
        {
            FirstName = createStudentDto.FirstName,
            LastName = createStudentDto.LastName,
            StudentNumber = createStudentDto.StudentNumber,
            GradeLevel = createStudentDto.GradeLevel,
            CreatedAt = DateTime.UtcNow
        };

        await _repository.AddAsync(student);
        await _repository.SaveChangesAsync();

        return new StudentDto
        {
            Id = student.Id,
            FirstName = student.FirstName,
            LastName = student.LastName,
            StudentNumber = student.StudentNumber,
            GradeLevel = student.GradeLevel,
            CreatedAt = student.CreatedAt
        };
    }

    // YENİ EKLENEN METOD: Güncelleme İşlemi
    public async Task UpdateStudentAsync(int id, CreateStudentDto updateStudentDto)
    {
        var student = await _repository.GetByIdAsync(id);
        if (student == null)
        {
            throw new Exception("Öğrenci bulunamadı.");
        }

        student.FirstName = updateStudentDto.FirstName;
        student.LastName = updateStudentDto.LastName;
        student.StudentNumber = updateStudentDto.StudentNumber;
        student.GradeLevel = updateStudentDto.GradeLevel;

        _repository.Update(student);
        await _repository.SaveChangesAsync();
    }

    // YENİ EKLENEN METOD: Silme İşlemi
    public async Task DeleteStudentAsync(int id)
    {
        var student = await _repository.GetByIdAsync(id);
        if (student == null)
        {
            throw new Exception("Öğrenci bulunamadı.");
        }

        _repository.Remove(student);
        await _repository.SaveChangesAsync();
    }
}