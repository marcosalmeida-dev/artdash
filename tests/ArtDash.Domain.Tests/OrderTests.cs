using ArtDash.Domain.Orders;

namespace ArtDash.Domain.Tests;

public sealed class OrderTests
{
    private static readonly DateTime Now = TestData.Now;

    [Fact]
    public void A_new_order_is_pending_with_totals_from_its_items_and_fee()
    {
        var order = TestData.Order(fee: 12m);

        order.Status.ShouldBe(OrderStatus.Pending);
        order.PaymentStatus.ShouldBe(PaymentStatus.Pending);
        order.Subtotal.ShouldBe(100m);
        order.DeliveryFee.ShouldBe(12m);
        order.Total.ShouldBe(112m);
        order.OutstandingAmount.ShouldBe(112m);
        order.StatusHistory.Count.ShouldBe(1);
    }

    [Fact]
    public void Adding_the_same_product_at_the_same_price_merges_the_line()
    {
        var order = TestData.Order();
        var productId = order.Items.Single().ProductId;

        order.AddItem(productId, "Produto", "SKU-1", UnitOfMeasure.Unit, 50m, 1);

        order.Items.Count.ShouldBe(1);
        order.Items.Single().Quantity.ShouldBe(3);
        order.Subtotal.ShouldBe(150m);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Quantity_must_be_positive(decimal quantity)
    {
        var order = TestData.Order();

        Should.Throw<DomainException>(() => order.AddItem(Guid.NewGuid(), "X", null, UnitOfMeasure.Unit, 1m, quantity));
    }

    [Fact]
    public void Discount_cannot_exceed_the_order_value()
    {
        var order = TestData.Order(fee: 10m);

        Should.Throw<DomainException>(() => order.ApplyDiscount(111m));
        order.ApplyDiscount(10m);
        order.Total.ShouldBe(100m);
    }

    [Fact]
    public void An_invoiced_order_needs_a_due_date()
    {
        var order = Order.Create("000002", "CODE", TestData.Customer(invoicing: true), OrderChannel.Backoffice,
            PaymentTiming.Invoiced, PaymentMethod.BankSlip, TestData.Address(), null, Now, "teste");
        order.AddItem(Guid.NewGuid(), "Produto", null, UnitOfMeasure.Unit, 10m, 1);

        Should.Throw<DomainException>(order.EnsureReadyToPlace);
    }

    [Fact]
    public void Only_invoiced_orders_have_a_due_date()
    {
        var order = TestData.Order(PaymentTiming.OnDelivery);

        Should.Throw<DomainException>(() => order.SetDueDate(DateOnly.FromDateTime(Now)));
    }

    [Fact]
    public void An_inactive_customer_cannot_order()
    {
        var customer = TestData.Customer();
        customer.IsActive = false;

        Should.Throw<DomainException>(() => Order.Create("000003", "CODE", customer, OrderChannel.Storefront,
            PaymentTiming.OnDelivery, null, TestData.Address(), null, Now, null));
    }

    [Fact]
    public void A_prepaid_order_cannot_be_confirmed_before_it_is_paid()
    {
        var order = TestData.Order(PaymentTiming.Prepaid);

        Should.Throw<DomainException>(() => order.Confirm(Now, "staff"));
        order.Status.ShouldBe(OrderStatus.Pending);
    }

    [Fact]
    public void A_prepaid_order_confirms_itself_when_fully_paid()
    {
        var order = TestData.Order(PaymentTiming.Prepaid);

        order.RegisterPayment(PaymentMethod.Pix, order.Total, Now, "MP-1", "Mercado Pago");

        order.PaymentStatus.ShouldBe(PaymentStatus.Paid);
        order.Status.ShouldBe(OrderStatus.Confirmed);
        order.PaidAt.ShouldBe(Now);
        order.ConfirmedAt.ShouldBe(Now);
    }

    [Fact]
    public void Partial_payments_add_up_until_the_order_is_paid()
    {
        var order = TestData.ConfirmedOrder(PaymentTiming.Invoiced);

        order.RegisterPayment(PaymentMethod.BankTransfer, 40m, Now, "TED 1", "staff");
        order.PaymentStatus.ShouldBe(PaymentStatus.PartiallyPaid);
        order.OutstandingAmount.ShouldBe(60m);

        order.RegisterPayment(PaymentMethod.BankTransfer, 60m, Now.AddDays(1), "TED 2", "staff");
        order.PaymentStatus.ShouldBe(PaymentStatus.Paid);
        order.OutstandingAmount.ShouldBe(0m);
        order.PaidAt.ShouldBe(Now.AddDays(1));
    }

    [Fact]
    public void A_paid_order_does_not_accept_another_payment()
    {
        var order = TestData.ConfirmedOrder(PaymentTiming.Prepaid);

        Should.Throw<DomainException>(() => order.RegisterPayment(PaymentMethod.Cash, 1m, Now, null, "staff"));
    }

    [Fact]
    public void The_same_gateway_transaction_is_registered_only_once()
    {
        var order = TestData.ConfirmedOrder(PaymentTiming.Invoiced);
        var transactionId = Guid.NewGuid();
        order.RegisterPayment(PaymentMethod.Pix, 10m, Now, null, "gateway", transactionId);

        Should.Throw<DomainException>(() => order.RegisterPayment(PaymentMethod.Pix, 10m, Now, null, "gateway", transactionId));
    }

    [Fact]
    public void Items_are_locked_once_the_order_is_confirmed()
    {
        var order = TestData.ConfirmedOrder();

        Should.Throw<DomainException>(() => order.AddItem(Guid.NewGuid(), "X", null, UnitOfMeasure.Unit, 1m, 1));
        Should.Throw<DomainException>(() => order.SetDeliveryFee(5m));
    }

    [Fact]
    public void Staff_moves_follow_the_lifecycle()
    {
        var order = TestData.ConfirmedOrder();

        order.MoveTo(OrderStatus.Preparing, Now, "staff");
        order.MoveTo(OrderStatus.ReadyForDelivery, Now, "staff");

        order.Status.ShouldBe(OrderStatus.ReadyForDelivery);
        order.StatusHistory.Select(h => h.ToStatus).ShouldBe(
            new[] { OrderStatus.Pending, OrderStatus.Confirmed, OrderStatus.Preparing, OrderStatus.ReadyForDelivery });
    }

    [Fact]
    public void A_pending_order_cannot_skip_to_preparing()
    {
        var order = TestData.Order();

        Should.Throw<DomainException>(() => order.MoveTo(OrderStatus.Preparing, Now, "staff"));
    }

    [Theory]
    [InlineData(OrderStatus.OutForDelivery)]
    [InlineData(OrderStatus.Delivered)]
    public void Out_for_delivery_and_delivered_come_only_from_a_run(OrderStatus target)
    {
        var order = TestData.ConfirmedOrder();

        Should.Throw<DomainException>(() => order.MoveTo(target, Now, "staff"));
    }

    [Fact]
    public void Cancelling_needs_a_reason()
    {
        var order = TestData.Order();

        Should.Throw<DomainException>(() => order.Cancel(" ", Now, "staff"));
        order.Cancel("Cliente desistiu", Now, "staff");

        order.Status.ShouldBe(OrderStatus.Cancelled);
        order.CancellationReason.ShouldBe("Cliente desistiu");
        order.IsFinal.ShouldBeTrue();
    }

    [Fact]
    public void An_order_planned_on_a_run_must_leave_it_before_being_cancelled()
    {
        var order = TestData.ConfirmedOrder();
        order.AssignToDelivery(Guid.NewGuid());

        Should.Throw<DomainException>(() => order.Cancel("Motivo", Now, "staff"));

        order.UnassignFromDelivery();
        order.Cancel("Motivo", Now, "staff");
        order.Status.ShouldBe(OrderStatus.Cancelled);
    }

    [Fact]
    public void A_delivered_order_cannot_be_cancelled()
    {
        var order = TestData.ConfirmedOrder();
        var deliveryId = Guid.NewGuid();
        order.MarkOutForDelivery(deliveryId, Now, "driver");
        order.MarkDelivered(Now.AddMinutes(30), "driver", null);

        order.Status.ShouldBe(OrderStatus.Delivered);
        order.DeliveredAt.ShouldBe(Now.AddMinutes(30));
        Should.Throw<DomainException>(() => order.Cancel("Motivo", Now, "staff"));
    }

    [Fact]
    public void A_failed_delivery_puts_the_order_back_in_the_queue()
    {
        var order = TestData.ConfirmedOrder();
        order.MarkOutForDelivery(Guid.NewGuid(), Now, "driver");

        order.ReturnFromFailedDelivery(Now, "driver", "Ninguém em casa");

        order.Status.ShouldBe(OrderStatus.ReadyForDelivery);
        order.DeliveryId.ShouldBeNull();
    }

    [Fact]
    public void An_order_cannot_be_on_two_runs()
    {
        var order = TestData.ConfirmedOrder();
        order.AssignToDelivery(Guid.NewGuid());

        Should.Throw<DomainException>(() => order.AssignToDelivery(Guid.NewGuid()));
    }

    [Fact]
    public void An_unpaid_invoice_past_its_due_date_is_overdue()
    {
        var order = TestData.ConfirmedOrder(PaymentTiming.Invoiced);
        var due = order.DueDate!.Value;

        order.IsOverdue(due).ShouldBeFalse();
        order.IsOverdue(due.AddDays(1)).ShouldBeTrue();

        order.RegisterPayment(PaymentMethod.BankSlip, order.Total, Now, null, "staff");
        order.IsOverdue(due.AddDays(1)).ShouldBeFalse();
    }

    [Fact]
    public void Tracking_codes_avoid_look_alike_characters()
    {
        var code = TrackingCodeGenerator.New();

        code.Length.ShouldBe(10);
        code.ShouldNotContain('0');
        code.ShouldNotContain('O');
        code.ShouldNotContain('1');
        code.ShouldNotContain('I');
        code.ShouldNotContain('L');
    }
}
