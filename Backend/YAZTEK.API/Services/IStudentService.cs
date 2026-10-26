using YAZTEK.API.Entities;

namespace YAZTEK.API.Services;

public interface IStudentService
{
    Task<IEnumerable<Student>> GetAllStudentsAsync();
    Task<Student?> GetStudentByIdAsync(int id);
    Task AddStudentAsync(Student student);
}