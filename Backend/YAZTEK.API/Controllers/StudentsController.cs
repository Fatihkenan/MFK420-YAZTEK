using Microsoft.AspNetCore.Mvc;
using YAZTEK.API.DTOs;
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

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var students = await _studentService.GetAllStudentsAsync();
        return Ok(students);
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(int id)
    {
        var student = await _studentService.GetStudentByIdAsync(id);
        if (student == null)
            return NotFound("Öğrenci bulunamadı.");

        return Ok(student);
    }

    [HttpPost]
    public async Task<IActionResult> Create(CreateStudentDto createStudentDto)
    {
        var createdStudent = await _studentService.AddStudentAsync(createStudentDto);
        return CreatedAtAction(nameof(GetById), new { id = createdStudent.Id }, createdStudent);
    }

    // YENİ: PUT - Güncelleme İşlemi
    [HttpPut("{id}")]
    public async Task<IActionResult> Update(int id, CreateStudentDto updateStudentDto)
    {
        try
        {
            await _studentService.UpdateStudentAsync(id, updateStudentDto);
            return NoContent(); // 204 No Content: İşlem başarılı ama geriye veri dönmeye gerek yok.
        }
        catch (Exception ex)
        {
            return NotFound(ex.Message);
        }
    }

    // YENİ: DELETE - Silme İşlemi
    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        try
        {
            await _studentService.DeleteStudentAsync(id);
            return NoContent();
        }
        catch (Exception ex)
        {
            return NotFound(ex.Message);
        }
    }
}