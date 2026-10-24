using ArtDash.Application.Common.Extensions;
using Microsoft.AspNetCore.Identity;

namespace ArtDash.Infrastructure.Persistence.Seeding;

/// <summary>
/// Demo data so a fresh tenant can be explored end to end: catalog, fleet, customers (with and without a
/// login, B2B with invoicing) and five weeks of orders in every state — paid online, collected at the door,
/// invoiced (paid, open and overdue), awaiting payment, being prepared, ready, out for delivery on a run with
/// a live GPS trail, delivered and cancelled.
/// Logins created here: motorista@{slug}.local / Motorista@123 and cliente@{slug}.local / Cliente@123.
/// Everything goes through the domain methods, so the data obeys the same rules as real orders.
/// The "today" part is relative to the moment the seed runs: recreate the database to refresh it.
/// </summary>
internal static class SampleData
{
    public static async Task SeedAsync(IServiceProvider services, ApplicationDbContext db, Tenant tenant, CancellationToken cancellationToken)
    {
        ConfigureStore(tenant);
        var catalog = AddCatalog(db);
        var (van, truck) = AddFleet(db);

        var market = NewCustomer(CustomerType.Company, "Mercadinho Bom Preço Ltda", "Mercadinho Bom Preço", "11988887777",
            NewAddress("04538133", "Avenida Brigadeiro Faria Lima", "3477", "Itaim Bibi", -23.5868, -46.6812),
            $"compras@bompreco.{tenant.Slug}.local", document: "12345678000195", invoicing: true, termDays: 28, creditLimit: 5000m);
        var bakery = NewCustomer(CustomerType.Company, "Padaria Pão Quente Ltda", "Padaria Pão Quente", "11977771111",
            NewAddress("05433000", "Rua Fradique Coutinho", "1100", "Vila Madalena", -23.5560, -46.6915),
            $"financeiro@paoquente.{tenant.Slug}.local", document: "98765432000198", invoicing: true, termDays: 21, creditLimit: 3000m);
        var bruno = NewCustomer(CustomerType.Individual, "Bruno Lima", null, "11955554444",
            NewAddress("04077000", "Alameda dos Maracatins", "780", "Moema", -23.6009, -46.6627), email: null);
        db.Customers.AddRange(market, bakery, bruno);

        await db.SaveChangesAsync(cancellationToken);

        var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();

        Driver? driver = null;
        var driverUser = await CreateUserAsync(userManager, tenant, $"motorista@{tenant.Slug}.local", "Carlos Motorista", "11977776666", "Motorista@123", AppRoles.Driver);
        if (driverUser is not null)
        {
            driver = new Driver
            {
                UserId = driverUser.Id,
                Name = driverUser.FullName,
                Phone = driverUser.PhoneNumber,
                LicenseNumber = "01234567890",
                LicenseCategory = "D",
                LicenseExpiresOn = DateOnly.FromDateTime(DateTime.Today.AddYears(3))
            };
            db.Drivers.Add(driver);
        }

        Customer? ana = null;
        var customerUser = await CreateUserAsync(userManager, tenant, $"cliente@{tenant.Slug}.local", "Ana Cliente", "11966665555", "Cliente@123", AppRoles.Customer);
        if (customerUser is not null)
        {
            ana = NewCustomer(CustomerType.Individual, customerUser.FullName, null, customerUser.PhoneNumber,
                NewAddress("05402000", "Rua Teodoro Sampaio", "1500", "Pinheiros", -23.5617, -46.6827, "Apto 12"), customerUser.Email);
            ana.UserId = customerUser.Id;
            db.Customers.Add(ana);
        }

        await db.SaveChangesAsync(cancellationToken);

        if (driver is null || ana is null)
        {
            return;
        }

        var customers = new SampleCustomers(ana, bruno, market, bakery);
        var seeder = new OrderSeeder(services.GetRequiredService<ISequenceGenerator>(), db, tenant, driver, van, truck, cancellationToken);
        await seeder.SeedAsync(catalog, customers);
    }

    private static void ConfigureStore(Tenant tenant)
    {
        tenant.Address = NewAddress("01310100", "Avenida Paulista", "1000", "Bela Vista", -23.5653, -46.6519);
        tenant.Store.Phone = "11999990000";
        tenant.Store.WhatsApp = "11999990000";
        tenant.Store.Email = $"contato@{tenant.Slug}.local";
        tenant.Store.OpeningHours = "Seg a Sex, 8h às 18h • Sáb, 8h às 12h";
        tenant.Store.DeliveryFee = 12m;
        tenant.Store.FreeDeliveryThreshold = 250m;
        tenant.Store.MinimumOrderAmount = 40m;
        tenant.Payments.PayOnDeliveryEnabled = true;
        tenant.Payments.InvoicingEnabled = true;
        tenant.Payments.DefaultPaymentTermDays = 28;
    }

    private static SampleCatalog AddCatalog(ApplicationDbContext db)
    {
        var beverages = new Category { Name = "Bebidas", Description = "Águas, refrigerantes e sucos", SortOrder = 1 };
        var grocery = new Category { Name = "Mercearia", Description = "Grãos, café e frios", SortOrder = 2 };
        var cleaning = new Category { Name = "Limpeza", Description = "Limpeza e descartáveis", SortOrder = 3 };
        db.Categories.AddRange(beverages, grocery, cleaning);

        var catalog = new SampleCatalog(
            Water: NewProduct(beverages, "Água mineral 500 ml (fardo 12)", "AGUA-500-12", 18.90m, UnitOfMeasure.Pack, 6.2m, featured: true, sortOrder: 1),
            Cola: NewProduct(beverages, "Refrigerante cola 2 L", "REFRI-COLA-2L", 9.49m, UnitOfMeasure.Unit, 2.1m, sortOrder: 2),
            Juice: NewProduct(beverages, "Suco de laranja integral 1 L", "SUCO-LAR-1L", 12.90m, UnitOfMeasure.Unit, 1.1m, compareAt: 14.90m, sortOrder: 3),
            Rice: NewProduct(grocery, "Arroz tipo 1 5 kg", "ARROZ-5KG", 27.90m, UnitOfMeasure.Pack, 5m, featured: true, sortOrder: 1),
            Beans: NewProduct(grocery, "Feijão carioca 1 kg", "FEIJAO-1KG", 8.99m, UnitOfMeasure.Pack, 1m, sortOrder: 2),
            Coffee: NewProduct(grocery, "Café torrado e moído 500 g", "CAFE-500", 19.90m, UnitOfMeasure.Pack, 0.5m, featured: true, sortOrder: 3),
            Cheese: NewProduct(grocery, "Queijo muçarela fatiado", "QUEIJO-MUC-KG", 54.90m, UnitOfMeasure.Kilogram, 1m, sortOrder: 4),
            Detergent: NewProduct(cleaning, "Detergente neutro 500 ml (caixa 24)", "DET-500-24", 54.00m, UnitOfMeasure.Box, 12.5m, sortOrder: 1),
            Towels: NewProduct(cleaning, "Papel toalha (pacote 2)", "PAPEL-TOALHA-2", 7.50m, UnitOfMeasure.Pack, 0.3m, sortOrder: 2));

        catalog.Juice.Description = "Sem adição de açúcar. Conservar refrigerado.";
        catalog.Cheese.Description = "Vendido por quilo: informe a quantidade em kg (ex.: 0,5).";

        db.Products.AddRange(catalog.All);
        return catalog;
    }

    private static (Vehicle Van, Vehicle Truck) AddFleet(ApplicationDbContext db)
    {
        var van = new Vehicle { Plate = "ABC1D23", Type = VehicleType.Van, Model = "Fiat Ducato", CapacityKg = 1500m };
        var truck = new Vehicle { Plate = "XYZ9K87", Type = VehicleType.Truck, Model = "VW Delivery 9.170", CapacityKg = 5000m };
        var motorcycle = new Vehicle { Plate = "MOT0A12", Type = VehicleType.Motorcycle, Model = "Honda CG 160 Cargo", CapacityKg = 30m };
        db.Vehicles.AddRange(van, truck, motorcycle);
        return (van, truck);
    }

    private static Address NewAddress(string zip, string street, string number, string district, double latitude, double longitude, string? complement = null) => new()
    {
        ZipCode = zip,
        Street = street,
        Number = number,
        Complement = complement,
        District = district,
        City = "São Paulo",
        State = "SP",
        Latitude = latitude,
        Longitude = longitude
    };

    private static Customer NewCustomer(CustomerType type, string name, string? tradeName, string? phone, Address address, string? email,
        string? document = null, bool invoicing = false, int termDays = 0, decimal? creditLimit = null) => new()
    {
        Type = type,
        Name = name,
        TradeName = tradeName,
        Document = document,
        Phone = phone,
        Email = email,
        Address = address,
        AllowInvoicing = invoicing,
        PaymentTermDays = termDays,
        CreditLimit = creditLimit
    };

    private static Product NewProduct(Category category, string name, string sku, decimal price, UnitOfMeasure unit, decimal weightKg,
        bool featured = false, decimal? compareAt = null, int sortOrder = 0) => new()
    {
        CategoryId = category.Id,
        Name = name,
        Sku = sku,
        Price = price,
        CompareAtPrice = compareAt,
        Unit = unit,
        WeightKg = weightKg,
        IsFeatured = featured,
        SortOrder = sortOrder
    };

    private static async Task<ApplicationUser?> CreateUserAsync(
        UserManager<ApplicationUser> userManager, Tenant tenant, string email, string name, string phone, string password, string role)
    {
        if (await userManager.FindByEmailAsync(email) is not null)
        {
            return null;
        }

        var user = new ApplicationUser
        {
            TenantId = tenant.Id,
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            FullName = name,
            PhoneNumber = phone,
            CreatedAt = DateTime.UtcNow,
            IsActive = true
        };

        var result = await userManager.CreateAsync(user, password);
        if (!result.Succeeded)
        {
            return null;
        }

        await userManager.AddToRoleAsync(user, role);
        return user;
    }

    private sealed record SampleCatalog(
        Product Water, Product Cola, Product Juice, Product Rice, Product Beans, Product Coffee,
        Product Cheese, Product Detergent, Product Towels)
    {
        public IEnumerable<Product> All => [Water, Cola, Juice, Rice, Beans, Coffee, Cheese, Detergent, Towels];
    }

    private sealed record SampleCustomers(Customer Ana, Customer Bruno, Customer Market, Customer Bakery);

    /// <summary>
    /// Orders and delivery runs, created in chronological order so order and run numbers grow with time.
    /// Past days are anchored to the tenant's business hours; "today" is relative to now.
    /// </summary>
    private sealed class OrderSeeder(
        ISequenceGenerator sequences,
        ApplicationDbContext db,
        Tenant tenant,
        Driver driver,
        Vehicle van,
        Vehicle truck,
        CancellationToken cancellationToken)
    {
        private const string Staff = "Equipe (dados de exemplo)";
        private const string Gateway = "Mercado Pago (exemplo)";

        private readonly DateTime _now = DateTime.UtcNow;
        private readonly TimeZoneInfo _zone = TenantClock.Resolve(tenant.Store.TimeZoneId);

        public async Task SeedAsync(SampleCatalog p, SampleCustomers c)
        {
            // Five weeks ago: an invoice paid only in part, now overdue.
            var overdue = await PlaceAsync(c.Bakery, PaymentTiming.Invoiced, PaymentMethod.BankSlip, At(-38, 9, 10),
                (p.Rice, 6), (p.Beans, 10), (p.Coffee, 8));
            overdue.Confirm(At(-38, 9, 20), Staff);
            await CompletedRunAsync(At(-37, 9, 30), van, (overdue, "Marcos (gerente)"));
            overdue.RegisterPayment(PaymentMethod.BankTransfer, 50m, At(-20, 15, 0), "TED parcial", Staff);

            // The last week: the sales chart, the order history and the past runs.
            var h1 = await PlaceAsync(c.Bruno, PaymentTiming.OnDelivery, PaymentMethod.Cash, At(-6, 9, 5),
                (p.Water, 2), (p.Rice, 1), (p.Beans, 2));
            h1.Confirm(At(-6, 9, 15), Staff);
            var h2 = await PlaceAsync(c.Market, PaymentTiming.Invoiced, PaymentMethod.BankSlip, At(-6, 10, 40),
                (p.Cola, 12), (p.Water, 6), (p.Towels, 6));
            h2.Confirm(At(-6, 10, 50), Staff);
            await CompletedRunAsync(At(-6, 14, 0), van, (h2, "Recebimento"), (h1, "Bruno"));
            h2.RegisterPayment(PaymentMethod.BankTransfer, h2.OutstandingAmount, At(-2, 11, 0), "TED", Staff);

            var h3 = await PlaceAsync(c.Ana, PaymentTiming.Prepaid, PaymentMethod.Pix, At(-5, 8, 30),
                (p.Coffee, 2), (p.Juice, 2), (p.Cheese, 0.5m));
            PayOnline(h3, PaymentMethod.Pix, At(-5, 8, 32));
            var h4 = await PlaceAsync(c.Bakery, PaymentTiming.Invoiced, PaymentMethod.BankSlip, At(-5, 9, 45),
                (p.Rice, 4), (p.Beans, 6), (p.Coffee, 4));
            h4.Confirm(At(-5, 10, 0), Staff);
            await CompletedRunAsync(At(-5, 13, 30), van, (h4, "Juliana"), (h3, "Ana"));

            var h5 = await PlaceAsync(c.Market, PaymentTiming.Invoiced, PaymentMethod.BankSlip, At(-4, 8, 10),
                (p.Water, 15), (p.Cola, 24));
            h5.Confirm(At(-4, 8, 20), Staff);
            var h6 = await PlaceAsync(c.Bruno, PaymentTiming.OnDelivery, PaymentMethod.DebitCard, At(-4, 11, 20),
                (p.Water, 1), (p.Cola, 4), (p.Towels, 2));
            h6.Confirm(At(-4, 11, 30), Staff);
            await CompletedRunAsync(At(-4, 14, 0), truck, (h5, "Recebimento"), (h6, "Bruno"));
            h5.RegisterPayment(PaymentMethod.BankSlip, h5.OutstandingAmount, At(-1, 10, 0), "Boleto compensado", Staff);

            var h7 = await PlaceAsync(c.Ana, PaymentTiming.OnDelivery, PaymentMethod.CreditCard, At(-3, 9, 0),
                (p.Cola, 4), (p.Towels, 2));
            h7.Cancel("Cliente desistiu da compra.", At(-3, 9, 25), Staff);
            var h8 = await PlaceAsync(c.Market, PaymentTiming.Invoiced, PaymentMethod.BankSlip, At(-3, 10, 15),
                (p.Detergent, 3), (p.Water, 12));
            h8.Confirm(At(-3, 10, 25), Staff);
            await CompletedRunAsync(At(-3, 13, 45), van, (h8, "Recebimento"));

            var h9 = await PlaceAsync(c.Ana, PaymentTiming.OnDelivery, PaymentMethod.Pix, At(-2, 9, 30),
                (p.Water, 2), (p.Coffee, 1), (p.Juice, 1));
            h9.Confirm(At(-2, 9, 40), Staff);
            var h10 = await PlaceAsync(c.Bakery, PaymentTiming.Invoiced, PaymentMethod.BankSlip, At(-2, 10, 0),
                (p.Cheese, 2), (p.Cola, 6));
            h10.Confirm(At(-2, 10, 10), Staff);
            await CompletedRunAsync(At(-2, 14, 15), van, (h10, "Juliana"), (h9, "Ana"));
            h10.RegisterPayment(PaymentMethod.BankSlip, h10.OutstandingAmount, At(-1, 9, 0), "Boleto compensado", Staff);

            // Yesterday: paid online, and an invoice still open (due in four weeks).
            var paid = await PlaceAsync(c.Ana, PaymentTiming.Prepaid, PaymentMethod.Pix, At(-1, 9, 10),
                (p.Water, 2), (p.Juice, 3), (p.Coffee, 1));
            PayOnline(paid, PaymentMethod.Pix, At(-1, 9, 12));
            var invoiced = await PlaceAsync(c.Market, PaymentTiming.Invoiced, PaymentMethod.BankSlip, At(-1, 10, 5),
                (p.Cola, 24), (p.Water, 10), (p.Detergent, 2));
            invoiced.Confirm(At(-1, 10, 15), Staff);
            await CompletedRunAsync(At(-1, 14, 0), van, (invoiced, "Recebimento"), (paid, "Ana"));

            // Today. Ready and waiting for a run.
            var ready = await PlaceAsync(c.Market, PaymentTiming.Invoiced, PaymentMethod.BankSlip, Ago(180),
                (p.Water, 20), (p.Cola, 12));
            ready.Confirm(Ago(175), Staff);
            ready.MoveTo(OrderStatus.ReadyForDelivery, Ago(60), Staff);

            // A run on the road right now: first stop done, the customer login's order is next.
            var first = await PlaceAsync(c.Bruno, PaymentTiming.OnDelivery, PaymentMethod.Cash, Ago(120),
                (p.Rice, 1), (p.Beans, 2), (p.Cheese, 0.5m), (p.Towels, 2));
            first.Confirm(Ago(115), Staff);
            var next = await PlaceAsync(c.Ana, PaymentTiming.OnDelivery, PaymentMethod.Pix, Ago(100),
                (p.Water, 1), (p.Coffee, 2), (p.Juice, 2));
            next.Confirm(Ago(95), Staff);
            await LiveRunAsync(first, next);

            // The queue on the order board.
            var preparing = await PlaceAsync(c.Bakery, PaymentTiming.Invoiced, PaymentMethod.BankSlip, Ago(45),
                (p.Cheese, 3), (p.Coffee, 6), (p.Cola, 6));
            preparing.Confirm(Ago(40), Staff);
            preparing.MoveTo(OrderStatus.Preparing, Ago(30), Staff);

            var confirmed = await PlaceAsync(c.Bruno, PaymentTiming.OnDelivery, PaymentMethod.CreditCard, Ago(25),
                (p.Water, 3), (p.Rice, 1));
            confirmed.Confirm(Ago(20), Staff);

            // Prepaid and not paid yet: stays pending until the customer pays.
            await PlaceAsync(c.Ana, PaymentTiming.Prepaid, PaymentMethod.Pix, Ago(12),
                (p.Coffee, 2), (p.Juice, 2), (p.Water, 1));

            // Just placed by phone: waiting for the staff to confirm.
            await PlaceAsync(c.Bakery, PaymentTiming.Invoiced, PaymentMethod.BankSlip, Ago(5),
                (p.Water, 5), (p.Towels, 10));

            await SaveAsync();
        }

        private async Task<Order> PlaceAsync(
            Customer customer, PaymentTiming timing, PaymentMethod? method, DateTime placedAt,
            params (Product Product, decimal Quantity)[] lines)
        {
            // The generator saves the context, so take the number before adding the order.
            var number = await sequences.NextAsync(SequenceNames.Order, cancellationToken);
            var fromStorefront = customer.UserId is not null;

            var order = Order.Create(
                number.ToString("D6"),
                TrackingCodeGenerator.New(),
                customer,
                fromStorefront ? OrderChannel.Storefront : OrderChannel.Backoffice,
                timing,
                method,
                customer.Address,
                null,
                placedAt,
                fromStorefront ? customer.Name : Staff);

            foreach (var (product, quantity) in lines)
            {
                order.AddItem(product.Id, product.Name, product.Sku, product.Unit, product.Price, quantity);
            }

            order.SetDeliveryFee(tenant.Store.DeliveryFeeFor(order.Subtotal));

            if (timing == PaymentTiming.Invoiced)
            {
                var term = customer.PaymentTermDays > 0 ? customer.PaymentTermDays : tenant.Payments.DefaultPaymentTermDays;
                order.SetDueDate(DateOnly.FromDateTime(TenantClock.ToLocal(placedAt, _zone)).AddDays(term));
            }

            order.EnsureReadyToPlace();
            db.Orders.Add(order);
            await SaveAsync();
            return order;
        }

        /// <summary>A prepaid order paid at checkout; the domain confirms it as soon as it is fully paid.</summary>
        private static void PayOnline(Order order, PaymentMethod method, DateTime at) =>
            order.RegisterPayment(method, order.Total, at, "Pagamento online (exemplo)", Gateway);

        /// <summary>A finished run: one stop every 25 minutes from <paramref name="start"/>.</summary>
        private async Task CompletedRunAsync(DateTime start, Vehicle vehicle, params (Order Order, string Recipient)[] stops)
        {
            var orders = stops.Select(s => s.Order).ToArray();
            var delivery = await PlanAsync(start, vehicle, orders);
            Start(delivery, start, orders);

            var at = start;
            foreach (var (order, recipient) in stops)
            {
                at = at.AddMinutes(25);
                Deliver(delivery, order, at, recipient);
            }

            delivery.Finish(at.AddMinutes(20));
            await SaveAsync();
        }

        /// <summary>
        /// Left the store 45 minutes ago, delivered the first stop in Moema and is heading to Pinheiros:
        /// shows on the live map, on the driver app and on both tracking pages.
        /// </summary>
        private async Task LiveRunAsync(Order first, Order next)
        {
            Order[] orders = [first, next];
            var delivery = await PlanAsync(Ago(50), van, orders);
            Start(delivery, Ago(45), orders);

            (int MinutesAgo, double Lat, double Lng, double Speed, double Heading)[] trail =
            [
                (45, -23.5653, -46.6519, 12, 200), (40, -23.5730, -46.6545, 28, 195), (35, -23.5815, -46.6570, 32, 195),
                (30, -23.5900, -46.6598, 25, 192), (25, -23.5975, -46.6620, 18, 190), (22, -23.6006, -46.6626, 4, 190),
                (15, -23.5930, -46.6680, 30, 330), (10, -23.5840, -46.6735, 34, 335), (5, -23.5750, -46.6780, 27, 338),
                (1, -23.5690, -46.6805, 22, 340)
            ];

            foreach (var fix in trail.Where(f => f.MinutesAgo > 20))
            {
                Record(delivery, fix.Lat, fix.Lng, fix.Speed, fix.Heading, Ago(fix.MinutesAgo));
            }

            Deliver(delivery, first, Ago(20), "Bruno");

            foreach (var fix in trail.Where(f => f.MinutesAgo <= 20))
            {
                Record(delivery, fix.Lat, fix.Lng, fix.Speed, fix.Heading, Ago(fix.MinutesAgo));
            }

            await SaveAsync();
        }

        private async Task<Delivery> PlanAsync(DateTime scheduledFor, Vehicle vehicle, Order[] orders)
        {
            var number = await sequences.NextAsync(SequenceNames.Delivery, cancellationToken);
            var delivery = Delivery.Plan($"E{number:D5}", driver, vehicle, scheduledFor, orders.Select(o => o.Id).ToList(), null);

            foreach (var order in orders)
            {
                order.AssignToDelivery(delivery.Id);
            }

            db.Deliveries.Add(delivery);
            await SaveAsync();
            return delivery;
        }

        private void Start(Delivery delivery, DateTime at, Order[] orders)
        {
            delivery.Start(at);
            foreach (var order in orders)
            {
                order.MarkOutForDelivery(delivery.Id, at, driver.Name);
            }
        }

        /// <summary>Same steps as the driver app: collect at the door when due, then close the stop.</summary>
        private void Deliver(Delivery delivery, Order order, DateTime at, string recipient)
        {
            var stop = delivery.Stops.First(s => s.OrderId == order.Id);
            delivery.CompleteStop(stop.Id, at, recipient, null);

            if (order.PaymentTiming == PaymentTiming.OnDelivery && order.OutstandingAmount > 0)
            {
                order.RegisterPayment(order.PaymentMethod ?? PaymentMethod.Cash, order.OutstandingAmount, at,
                    $"Recebido na entrega {delivery.Number}", driver.Name);
            }

            order.MarkDelivered(at, driver.Name, $"Recebido por {recipient}");
        }

        private void Record(Delivery delivery, double latitude, double longitude, double speedKmh, double heading, DateTime at)
        {
            delivery.RecordPosition(latitude, longitude, speedKmh, heading, at);
            db.VehiclePositions.Add(new VehiclePosition(delivery.Id, driver.Id, delivery.VehicleId, latitude, longitude, 8, speedKmh, heading, at));
        }

        /// <summary>A local time on a day of the tenant's calendar (offset from today), in UTC.</summary>
        private DateTime At(int dayOffset, int hour, int minute)
        {
            var local = TenantClock.LocalToday(_now, _zone).AddDays(dayOffset).AddHours(hour).AddMinutes(minute);
            return TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), _zone);
        }

        private DateTime Ago(int minutes) => _now.AddMinutes(-minutes);

        private Task SaveAsync() => db.SaveChangesAsync(cancellationToken);
    }
}
