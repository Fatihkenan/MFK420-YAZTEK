using Microsoft.EntityFrameworkCore;
using YAZTEK.API.Entities;
using YAZTEK.API.Persistence;

namespace YAZTEK.API.Repositories;

public class StudentRepository : IStudentRepository
{
    private readonly YaztekDbContext _context;

    public StudentRepository(YaztekDbContext context)
    {
        _context = context;
    }

    public async Task<IEnumerable<Student>> GetAllAsync()
    {
        return await _context.Students.ToListAsync();
    }

    public async Task<Student?> GetByIdAsync(int id)
    {
        return await _context.Students.FindAsync(id);
    }

    public async Task AddAsync(Student student)
    {
        await _context.Students.AddAsync(student);
    }

    public async Task SaveChangesAsync()
    {
        await _context.SaveChangesAsync();
    }
}