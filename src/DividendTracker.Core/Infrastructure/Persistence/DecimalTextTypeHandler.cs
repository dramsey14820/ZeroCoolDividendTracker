using System.Data;
using System.Globalization;
using Dapper;

namespace DividendTracker.Core.Infrastructure.Persistence;

public sealed class DecimalTextTypeHandler : SqlMapper.TypeHandler<decimal>
{
    public override void SetValue(IDbDataParameter parameter, decimal value)
    {
        ArgumentNullException.ThrowIfNull(parameter);
        parameter.Value = value.ToString(CultureInfo.InvariantCulture);
        parameter.DbType = DbType.String;
    }

    public override decimal Parse(object value)
    {
        return value switch
        {
            decimal d => d,
            string s => decimal.Parse(s, CultureInfo.InvariantCulture),
            double dbl => Convert.ToDecimal(dbl, CultureInfo.InvariantCulture),
            long l => l,
            int i => i,
            _ => throw new ArgumentException(
                $"Cannot convert value of type '{value?.GetType().Name ?? "null"}' to decimal.",
                nameof(value))
        };
    }
}

