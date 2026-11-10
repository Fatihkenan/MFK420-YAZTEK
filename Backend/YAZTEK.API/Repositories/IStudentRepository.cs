using YAZTEK.API.Entities;

namespace YAZTEK.API.Repositories;

public interface IStudentRepository
{
    Task<IEnumerable<Student>> GetAllAsync();
    Task<Student?> GetByIdAsync(int id);
    Task AddAsync(Student student);
    void Update(Student student);
    void Remove(Student student);
    Task SaveChangesAsync();
}