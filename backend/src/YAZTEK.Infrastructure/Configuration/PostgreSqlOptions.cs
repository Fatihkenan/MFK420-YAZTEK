namespace YAZTEK.Infrastructure.Configuration;

public sealed class PostgreSqlOptions
{
    public const string SectionName = "PostgreSql";

    public string ConnectionStringName { get; init; } = "PostgreSql";
}
