using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MuktoAin.Domain.Entities;

namespace MuktoAin.Infrastructure.Data.Configurations;

public class PaymentOrderConfiguration : IEntityTypeConfiguration<PaymentOrder>
{
    public void Configure(EntityTypeBuilder<PaymentOrder> builder)
    {
        builder.ToTable("PAYMENT_ORDER", "dbo");
        builder.HasKey(o => o.PaymentOrderId);

        builder.Property(o => o.Amount).HasPrecision(10, 2);
        builder.Property(o => o.Commission).HasPrecision(10, 2);
        builder.Property(o => o.NetToLawyer).HasPrecision(10, 2);

        // scripts/18_payment_gateway.sql
        builder.Property(o => o.TransactionId).HasMaxLength(100);
        builder.Property(o => o.RowVersion).IsRowVersion();

        // scripts/19_payment_gateway_routing.sql
        builder.Property(o => o.GatewaySessionId).HasMaxLength(100);
        // ChatCredits: scripts/20_ai_chat_credits.sql, mapped by convention.
    }
}
