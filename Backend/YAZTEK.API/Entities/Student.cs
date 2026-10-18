namespace YAZTEK.API.Entities
{
    public class Student
    {
        public int Id { get; set; }
        public String? FirstName { get; set; }
        public string? LastName { get; set; }
        public string? StudentNumber { get; set; }
        public int GradeLevel { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
