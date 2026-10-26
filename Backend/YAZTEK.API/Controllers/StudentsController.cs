using Microsoft.AspNetCore.Mvc;
using YAZTEK.API.Entities;
using YAZTEK.API.Services;

namespace YAZTEK.API.Controllers;

[Route("api/[controller]")]
[ApiController]
public class StudentsController : ControllerBase
{
    private readonly IStudentService _studentService;

    public StudentsController(IStudentService studentService)
    {
        _studentService = studentService;
    }

    // GET: api/students (Tüm öğrencileri getirir)
    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var students = await _studentService.GetAllStudentsAsync();
        return Ok(students);
    }

    // GET: api/students/5 (Belirli bir öğrenciyi getirir)
    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(int id)
    {
        var student = await _studentService.GetStudentByIdAsync(id);
        if (student == null)
            return NotFound("Öğrenci bulunamadı.");

        return Ok(student);
    }

    // POST: api/students (Yeni öğrenci ekler)
    [HttpPost]
    public async Task<IActionResult> Create(Student student)
    {
        await _studentService.AddStudentAsync(student);

        // Eklendikten sonra HTTP 201 Created döner ve veriyi gösterir
        return CreatedAtAction(nameof(GetById), new { id = student.Id }, student);
    }
}