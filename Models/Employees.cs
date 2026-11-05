namespace orderManagementSystem.Models;

public class Employees
{
    public int Id { get; set; }

//No idea how normalization applies to an accounts table?
    public string Name { get; set; } = "";
    public string Password { get; set; }
    public string Role { get; set; }
    public string Status { get; set; } = "Active";
}