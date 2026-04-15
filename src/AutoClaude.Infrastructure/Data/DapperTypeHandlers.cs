using System.Data;
using System.Globalization;
using Dapper;

namespace AutoClaude.Infrastructure.Data;

public class GuidTypeHandler : SqlMapper.TypeHandler<Guid>
{
    public override void SetValue(IDbDataParameter parameter, Guid value)
        => parameter.Value = value.ToString();

    public override Guid Parse(object value)
        => Guid.Parse((string)value);
}

public class NullableGuidTypeHandler : SqlMapper.TypeHandler<Guid?>
{
    public override void SetValue(IDbDataParameter parameter, Guid? value)
        => parameter.Value = value?.ToString() ?? (object)DBNull.Value;

    public override Guid? Parse(object value)
        => value is null or DBNull ? null : Guid.Parse((string)value);
}

public class DateTimeTypeHandler : SqlMapper.TypeHandler<DateTime>
{
    public override void SetValue(IDbDataParameter parameter, DateTime value)
        => parameter.Value = value.ToString("o");

    public override DateTime Parse(object value)
        => DateTime.Parse((string)value, null, DateTimeStyles.RoundtripKind);
}

public class NullableDateTimeTypeHandler : SqlMapper.TypeHandler<DateTime?>
{
    public override void SetValue(IDbDataParameter parameter, DateTime? value)
        => parameter.Value = value?.ToString("o") ?? (object)DBNull.Value;

    public override DateTime? Parse(object value)
        => value is null or DBNull ? null : DateTime.Parse((string)value, null, DateTimeStyles.RoundtripKind);
}

public static class DapperTypeHandlerRegistration
{
    private static bool _registered;

    public static void Register()
    {
        if (_registered) return;

        SqlMapper.AddTypeHandler(new GuidTypeHandler());
        SqlMapper.AddTypeHandler(new NullableGuidTypeHandler());
        SqlMapper.AddTypeHandler(new DateTimeTypeHandler());
        SqlMapper.AddTypeHandler(new NullableDateTimeTypeHandler());

        _registered = true;
    }
}
