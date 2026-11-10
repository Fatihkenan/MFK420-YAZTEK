namespace YAZTEK.API.DTOs;

public class StudentDto
{
    public int Id { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string StudentNumber { get; set; } = string.Empty;
    public int GradeLevel { get; set; }
    public DateTime CreatedAt { get; set; }
}