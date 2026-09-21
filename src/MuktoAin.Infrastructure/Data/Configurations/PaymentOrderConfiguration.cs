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
    }
}
