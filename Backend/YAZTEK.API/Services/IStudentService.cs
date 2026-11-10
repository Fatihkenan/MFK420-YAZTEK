using YAZTEK.API.DTOs;

namespace YAZTEK.API.Services;

public interface IStudentService
{
    Task<IEnumerable<StudentDto>> GetAllStudentsAsync();
    Task<StudentDto?> GetStudentByIdAsync(int id);
    Task<StudentDto> AddStudentAsync(CreateStudentDto createStudentDto);

    // YENİ EKLENEN SATIRLAR
    Task UpdateStudentAsync(int id, CreateStudentDto updateStudentDto);
    Task DeleteStudentAsync(int id);
}