using ArtDash.Domain.Deliveries;

namespace ArtDash.Domain.Tests;

public sealed class DeliveryTests
{
    private static readonly DateTime Now = TestData.Now;

    private static Delivery Plan(int stops = 2) =>
        Delivery.Plan("E00001", TestData.Driver(), TestData.Vehicle(), Now,
            Enumerable.Range(0, stops).Select(_ => Guid.NewGuid()).ToList(), null);

    [Fact]
    public void A_run_is_planned_with_its_stops_in_order()
    {
        var delivery = Plan(3);

        delivery.Status.ShouldBe(DeliveryStatus.Planned);
        delivery.Stops.Select(s => s.Sequence).ShouldBe(new[] { 1, 2, 3 });
        delivery.Stops.ShouldAllBe(s => s.Status == DeliveryStopStatus.Pending);
    }

    [Fact]
    public void A_run_needs_at_least_one_order_and_no_repeats()
    {
        var orderId = Guid.NewGuid();

        Should.Throw<DomainException>(() => Delivery.Plan("E1", TestData.Driver(), TestData.Vehicle(), Now, [], null));
        Should.Throw<DomainException>(() => Delivery.Plan("E1", TestData.Driver(), TestData.Vehicle(), Now, [orderId, orderId], null));
    }

    [Fact]
    public void Inactive_drivers_and_vehicles_cannot_be_planned()
    {
        var driver = TestData.Driver();
        driver.IsActive = false;
        var vehicle = TestData.Vehicle();
        vehicle.IsActive = false;

        Should.Throw<DomainException>(() => Delivery.Plan("E1", driver, TestData.Vehicle(), Now, [Guid.NewGuid()], null));
        Should.Throw<DomainException>(() => Delivery.Plan("E1", TestData.Driver(), vehicle, Now, [Guid.NewGuid()], null));
    }

    [Fact]
    public void Positions_are_only_recorded_while_in_progress()
    {
        var delivery = Plan();

        Should.Throw<DomainException>(() => delivery.RecordPosition(-23.5, -46.6, 30, 90, Now));

        delivery.Start(Now);
        delivery.RecordPosition(-23.5, -46.6, 30, 90, Now.AddMinutes(1));

        delivery.LastLatitude.ShouldBe(-23.5);
        delivery.LastPositionAt.ShouldBe(Now.AddMinutes(1));
    }

    [Fact]
    public void A_late_position_never_replaces_a_newer_one()
    {
        var delivery = Plan();
        delivery.Start(Now);
        delivery.RecordPosition(-23.50, -46.60, 30, 90, Now.AddMinutes(5));

        delivery.RecordPosition(-23.40, -46.50, 30, 90, Now.AddMinutes(2));

        delivery.LastLatitude.ShouldBe(-23.50);
        delivery.LastPositionAt.ShouldBe(Now.AddMinutes(5));
    }

    [Theory]
    [InlineData(91, 0)]
    [InlineData(0, 181)]
    public void Invalid_coordinates_are_rejected(double latitude, double longitude)
    {
        var delivery = Plan();
        delivery.Start(Now);

        Should.Throw<DomainException>(() => delivery.RecordPosition(latitude, longitude, null, null, Now));
    }

    [Fact]
    public void A_run_finishes_only_when_every_stop_is_closed()
    {
        var delivery = Plan();
        delivery.Start(Now);
        var stops = delivery.Stops.ToList();

        delivery.CompleteStop(stops[0].Id, Now.AddMinutes(20), "Ana", null);
        Should.Throw<DomainException>(() => delivery.Finish(Now.AddMinutes(30)));

        delivery.FailStop(stops[1].Id, Now.AddMinutes(40), "Ninguém em casa");
        delivery.Finish(Now.AddMinutes(50));

        delivery.Status.ShouldBe(DeliveryStatus.Completed);
        stops[0].Status.ShouldBe(DeliveryStopStatus.Delivered);
        stops[0].RecipientName.ShouldBe("Ana");
        stops[1].Status.ShouldBe(DeliveryStopStatus.Failed);
    }

    [Fact]
    public void A_stop_is_closed_only_once()
    {
        var delivery = Plan(1);
        delivery.Start(Now);
        var stop = delivery.Stops.Single();
        delivery.CompleteStop(stop.Id, Now, null, null);

        Should.Throw<DomainException>(() => delivery.FailStop(stop.Id, Now, "Motivo"));
    }

    [Fact]
    public void Only_a_planned_run_can_be_cancelled()
    {
        var planned = Plan();
        planned.Cancel("Veículo quebrou", Now);
        planned.Status.ShouldBe(DeliveryStatus.Cancelled);

        var started = Plan();
        started.Start(Now);
        Should.Throw<DomainException>(() => started.Cancel("Motivo", Now));
    }
}
