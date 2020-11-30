using System.Collections.Generic;
using System.Linq;
using FluentValidation;
using VirtoCommerce.CartModule.Core.Model;
using VirtoCommerce.PaymentModule.Core.Model;
using VirtoCommerce.Platform.Core.Common;

namespace VirtoCommerce.XPurchase.Validators
{
    public class CartPaymentValidator : AbstractValidator<Payment>
    {
        public CartPaymentValidator(IEnumerable<PaymentMethod> availPaymentMethods)
        {
            //To support the use case for partial payment update when user sets the address first.
            //RuleFor(x => x.PaymentGatewayCode).NotNull().NotEmpty();
            RuleSet("strict", () =>
            {
                RuleFor(x => x).Custom((payment, context) =>
                {
                    if (availPaymentMethods != null && !string.IsNullOrEmpty(payment.PaymentGatewayCode))
                    {
                        var paymentMethod = availPaymentMethods.FirstOrDefault(x => payment.PaymentGatewayCode.EqualsInvariant(x.Code));
                        if (paymentMethod == null)
                        {
                            context.AddFailure(CartErrorDescriber.PaymentMethodUnavailable(payment, payment.PaymentGatewayCode));
                        }
                    }
                });
            });
        }
    }
}
