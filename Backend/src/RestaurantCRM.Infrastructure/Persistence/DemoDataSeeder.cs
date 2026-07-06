using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using RestaurantCRM.Domain.Entities;
using RestaurantCRM.Domain.Enums;

namespace RestaurantCRM.Infrastructure.Persistence;

/// <summary>
/// Seeds one self-contained, realistic demo restaurant ("Tsirani", a Yerevan
/// tavern) so a fresh database isn't empty. Idempotent: keyed on the admin email,
/// it no-ops if that account already exists, so it's safe to run on every boot /
/// deploy. All money is AMD; all timestamps are UTC and backdated so reports,
/// revenue trends and the activity log have history to show.
/// </summary>
public static class DemoDataSeeder
{
    public const string AdminEmail = "admin@tsirani.am";
    private const string DemoPassword = "demo1234";

    public static async Task SeedAsync(AppDbContext db, ILogger logger, CancellationToken ct = default)
    {
        var already = await db.Users.IgnoreQueryFilters().AnyAsync(u => u.Email == AdminEmail, ct);
        if (already)
        {
            logger.LogInformation("Demo data already present (found {Email}); skipping seed.", AdminEmail);
            return;
        }

        logger.LogInformation("Seeding demo restaurant 'Tsirani'…");
        var rng = new Random(20260613);
        var hasher = new PasswordHasher<User>();
        var now = DateTime.UtcNow;

        // ── Restaurant ───────────────────────────────────────────────────────
        var restaurant = new Restaurant
        {
            Name = "Tsirani",
            LegalName = "Tsirani Tavern LLC",
            Currency = "AMD",
            Address = "Northern Avenue 5, Yerevan 0001",
            Phone = "+374 10 54 54 54",
        };
        db.Restaurants.Add(restaurant);

        // ── Roles + permissions (same defaults as self-signup) ───────────────
        var roles = DefaultRolePermissions.Map.Select(entry => new Role
        {
            RestaurantId = restaurant.Id,
            Name = entry.Key,
            IsDefault = true,
            RolePermissions = [.. entry.Value.Select(p => new RolePermission { Permission = p })],
        }).ToList();
        db.Roles.AddRange(roles);
        Role Role(string name) => roles.First(r => r.Name == name);

        // ── Staff ────────────────────────────────────────────────────────────
        var staffSpec = new (string First, string Last, string Father, string Role, string Phone)[]
        {
            ("Davit",  "Sargsyan",     "Aramovich",     "Admin",     "+374 91 100 100"),
            ("Anna",   "Hakobyan",     "Gevorgovna",    "Manager",   "+374 91 100 101"),
            ("Narek",  "Grigoryan",    "Arturovich",    "Waiter",    "+374 91 100 102"),
            ("Mariam", "Petrosyan",    "Davitovna",     "Waiter",    "+374 91 100 103"),
            ("Gor",    "Avetisyan",    "Hovikovich",    "Waiter",    "+374 91 100 104"),
            ("Lilit",  "Khachatryan",  "Surenovna",     "Waiter",    "+374 91 100 105"),
            ("Hayk",   "Manukyan",     "Vardanovich",   "Cook",      "+374 91 100 106"),
            ("Tigran", "Hovhannisyan", "Ashotovich",    "Cook",      "+374 91 100 107"),
            ("Aram",   "Vardanyan",    "Levonovich",    "Bartender", "+374 91 100 108"),
            ("Nune",   "Karapetyan",   "Samvelovna",    "Cashier",   "+374 91 100 109"),
            ("Sona",   "Mkrtchyan",    "Robertovna",    "Cashier",   "+374 91 100 110"),
        };
        var staff = new List<User>();
        foreach (var s in staffSpec)
        {
            var u = new User
            {
                RestaurantId = restaurant.Id,
                RoleId = Role(s.Role).Id,
                FirstName = s.First,
                LastName = s.Last,
                FatherName = s.Father,
                Email = $"{s.First}.{s.Last}@tsirani.am".ToLowerInvariant(),
                Phone = s.Phone,
                Status = UserStatus.Active,
            };
            u.PasswordHash = hasher.HashPassword(u, DemoPassword);
            staff.Add(u);
        }
        // The login the user can actually use. Admin = first entry.
        var admin = staff[0];
        admin.Email = AdminEmail;
        admin.PasswordHash = hasher.HashPassword(admin, DemoPassword);
        db.Users.AddRange(staff);

        var waiters = staff.Where(u => u.RoleId == Role("Waiter").Id || u.RoleId == Role("Bartender").Id).ToList();
        var cashiers = staff.Where(u => u.RoleId == Role("Cashier").Id).ToList();

        // ── Menu (categories + items), prices in AMD ─────────────────────────
        var menu = new (string Category, (string Name, int Price, string? Desc)[] Items)[]
        {
            ("Appetizers", new[]
            {
                ("Lavash & Cheese Plate", 2200, (string?)"Fresh lavash, lori cheese, herbs"),
                ("Basturma", 3400, "Air-cured beef, thinly sliced"),
                ("Sujukh Platter", 3200, "Spiced cured sausage"),
                ("Eggplant Rolls with Walnut", 2600, "Garlic-walnut filling"),
                ("Ajika & Bread", 1500, "Spicy pepper paste"),
                ("Pickled Vegetables", 1400, (string?)null),
            }),
            ("Salads", new[]
            {
                ("Greek Salad", 2400, (string?)null),
                ("Ateni Village Salad", 2200, "Tomato, cucumber, onion, herbs"),
                ("Tabbouleh", 2300, "Parsley, bulgur, lemon"),
                ("Beetroot & Walnut", 2100, (string?)null),
                ("Caesar with Chicken", 3200, (string?)null),
            }),
            ("Soups", new[]
            {
                ("Spas (yogurt soup)", 1800, "Traditional matsun soup"),
                ("Khash", 4500, "Winter morning specialty"),
                ("Bozbash", 2800, "Lamb and chickpea soup"),
                ("Mushroom Cream Soup", 2200, (string?)null),
            }),
            ("Khorovats (BBQ)", new[]
            {
                ("Pork Khorovats", 4800, "Charcoal-grilled pork"),
                ("Chicken Khorovats", 4200, (string?)null),
                ("Lamb Ribs", 6500, "Marinated, charcoal-grilled"),
                ("Khorovats Platter for 2", 11500, "Mixed grill, serves two"),
                ("Grilled Vegetables (Ikra)", 2600, "Smoky eggplant, pepper, tomato"),
                ("Lula Kebab", 3900, "Minced lamb skewer"),
            }),
            ("Main Dishes", new[]
            {
                ("Khinkali (5 pcs)", 3000, "Hand-folded dumplings"),
                ("Dolma", 3400, "Grape leaves, minced meat & rice"),
                ("Harissa", 2800, "Slow-cooked wheat and chicken"),
                ("Ghapama", 3600, "Stuffed pumpkin, rice, dried fruit"),
                ("Tolma in Matsun", 3500, (string?)null),
                ("Pan-Fried Trout (Ishkhan)", 5800, "Lake Sevan trout"),
                ("Beef Stroganoff", 4200, (string?)null),
            }),
            ("Desserts", new[]
            {
                ("Gata", 1300, "Sweet pastry"),
                ("Pakhlava", 1600, "Layered nut pastry"),
                ("Napoleon Cake", 1800, (string?)null),
                ("Honey Cake (Medovik)", 1900, (string?)null),
                ("Sujukh Sweet (Churchkhela)", 1200, "Walnut & grape must"),
                ("Ice Cream (3 scoops)", 1500, (string?)null),
            }),
            ("Drinks", new[]
            {
                ("Tan (yogurt drink)", 600, (string?)null),
                ("Armenian Coffee", 800, "Brewed in jazve"),
                ("Areni Red Wine (glass)", 1800, "Dry, from Vayots Dzor"),
                ("Kilikia Beer 0.5L", 1200, (string?)null),
                ("Jermuk Sparkling Water", 700, (string?)null),
                ("Fruit Compote", 900, (string?)null),
                ("Fresh Lemonade", 1100, "Tarragon or classic"),
                ("Black Tea", 700, (string?)null),
            }),
            ("Cold Starters", new[]
            {
                ("Khash-style Garlic Sauce", 800, (string?)null),
                ("Smoked Trout Plate", 4600, (string?)null),
                ("Cheese Selection", 3800, "Lori, chanakh, motal"),
            }),
        };

        var menuItems = new List<MenuItem>();
        int sort = 0;
        foreach (var (catName, items) in menu)
        {
            var cat = new MenuCategory
            {
                RestaurantId = restaurant.Id,
                Name = catName,
                SortOrder = sort++,
                Station = catName == "Drinks" ? Station.Bar : Station.Kitchen,
            };
            db.MenuCategories.Add(cat);
            foreach (var (name, price, desc) in items)
            {
                var mi = new MenuItem
                {
                    RestaurantId = restaurant.Id,
                    CategoryId = cat.Id,
                    Name = name,
                    Description = desc,
                    Price = price,
                    // A couple of items out of stock for realism.
                    IsAvailable = !(name == "Khash" || name == "Lamb Ribs" && rng.Next(2) == 0),
                };
                menuItems.Add(mi);
                db.MenuItems.Add(mi);
            }
        }

        // ── Tables ───────────────────────────────────────────────────────────
        var tableSpec = new (int Number, int Capacity, bool Vip, int VipAmount)[]
        {
            (1, 2, false, 0), (2, 2, false, 0), (3, 4, false, 0), (4, 4, false, 0),
            (5, 4, false, 0), (6, 4, false, 0), (7, 6, false, 0), (8, 6, false, 0),
            (9, 6, false, 0), (10, 8, false, 0), (11, 8, false, 0), (12, 4, false, 0),
            (101, 6, true, 20000), (102, 10, true, 35000),
        };
        var tables = tableSpec.Select(t => new Table
        {
            RestaurantId = restaurant.Id,
            Number = t.Number,
            Capacity = t.Capacity,
            IsVip = t.Vip,
            VipAmount = t.VipAmount,
            Status = TableStatus.Free,
        }).ToList();
        db.Tables.AddRange(tables);

        // ── Clients (with deposit ledger) ────────────────────────────────────
        var clientSpec = new (string Name, string Phone, LoyaltyType Loyalty, int Rate, int Balance)[]
        {
            ("Armen Petrosyan",     "+374 93 200 201", LoyaltyType.Cashback, 5, 15000),
            ("Lusine Avagyan",      "+374 93 200 202", LoyaltyType.Cashback, 5, 8200),
            ("Vahram Ghazaryan",    "+374 93 200 203", LoyaltyType.None,     0, 0),
            ("Karine Sahakyan",     "+374 93 200 204", LoyaltyType.Cashback, 10, 42000),
            ("Suren Melikyan",      "+374 93 200 205", LoyaltyType.None,     0, -6500),
            ("Gohar Minasyan",      "+374 93 200 206", LoyaltyType.Cashback, 5, 3100),
            ("Ruben Tadevosyan",    "+374 93 200 207", LoyaltyType.None,     0, 0),
            ("Ani Stepanyan",       "+374 93 200 208", LoyaltyType.Cashback, 7, 26500),
            ("Hovhannes Galstyan",  "+374 93 200 209", LoyaltyType.None,     0, 12000),
            ("Mariam Yeghiazaryan", "+374 93 200 210", LoyaltyType.Cashback, 5, 0),
            ("Edgar Harutyunyan",   "+374 93 200 211", LoyaltyType.None,     0, -2200),
            ("Diana Mkhitaryan",    "+374 93 200 212", LoyaltyType.Cashback, 5, 9400),
            ("Vardan Asatryan",     "+374 93 200 213", LoyaltyType.None,     0, 0),
            ("Tatevik Simonyan",    "+374 93 200 214", LoyaltyType.Cashback, 10, 58000),
            ("Aram Israyelyan",     "+374 93 200 215", LoyaltyType.None,     0, 4500),
            ("Nare Babayan",        "+374 93 200 216", LoyaltyType.Cashback, 5, 1800),
            ("Levon Poghosyan",     "+374 93 200 217", LoyaltyType.None,     0, 0),
            ("Christine Davtyan",   "+374 93 200 218", LoyaltyType.Cashback, 6, 7700),
            ("Garik Movsisyan",     "+374 93 200 219", LoyaltyType.None,     0, -1500),
            ("Elen Aleksanyan",     "+374 93 200 220", LoyaltyType.Cashback, 5, 33200),
            ("Artak Barseghyan",    "+374 93 200 221", LoyaltyType.None,     0, 0),
            ("Marine Hambardzumyan","+374 93 200 222", LoyaltyType.Cashback, 8, 19000),
        };
        var clients = new List<Client>();
        foreach (var c in clientSpec)
        {
            var client = new Client
            {
                RestaurantId = restaurant.Id,
                FullName = c.Name,
                Phone = c.Phone,
                LoyaltyType = c.Loyalty,
                LoyaltyRate = c.Rate,
                DepositBalance = c.Balance,
                Birthday = new DateOnly(1980 + rng.Next(25), 1 + rng.Next(12), 1 + rng.Next(28)),
            };
            clients.Add(client);
            db.Clients.Add(client);

            // Back the non-zero balance with a ledger row so it isn't a phantom number.
            if (c.Balance != 0)
            {
                db.Set<ClientTransaction>().Add(new ClientTransaction
                {
                    RestaurantId = restaurant.Id,
                    ClientId = client.Id,
                    CreatedById = cashiers[rng.Next(cashiers.Count)].Id,
                    Type = c.Balance > 0 ? ClientTransactionType.Deposit : ClientTransactionType.OrderPayment,
                    Amount = c.Balance,
                    BalanceAfter = c.Balance,
                    Reason = c.Balance > 0 ? "Account top-up" : "On credit",
                    CreatedAt = now.AddDays(-rng.Next(10, 40)),
                });
            }
        }

        // ── Warehouse products + stock movements ─────────────────────────────
        var productSpec = new (string Name, string Category, ProductUnit Unit, int Stock, int Low)[]
        {
            ("Pork (shoulder)",   "Meat",      ProductUnit.Kg,    42, 10),
            ("Chicken",           "Meat",      ProductUnit.Kg,    35, 10),
            ("Lamb",              "Meat",      ProductUnit.Kg,    18, 8),
            ("Lake Sevan Trout",  "Fish",      ProductUnit.Kg,    6,  5),
            ("Lavash",            "Bakery",    ProductUnit.Piece, 120, 40),
            ("Lori Cheese",       "Dairy",     ProductUnit.Kg,    14, 5),
            ("Matsun (yogurt)",   "Dairy",     ProductUnit.Liter, 20, 8),
            ("Tomatoes",          "Produce",   ProductUnit.Kg,    28, 10),
            ("Cucumbers",         "Produce",   ProductUnit.Kg,    22, 8),
            ("Eggplant",          "Produce",   ProductUnit.Kg,    16, 6),
            ("Potatoes",          "Produce",   ProductUnit.Kg,    50, 15),
            ("Onions",            "Produce",   ProductUnit.Kg,    30, 10),
            ("Rice",              "Dry Goods", ProductUnit.Kg,    25, 10),
            ("Flour",             "Dry Goods", ProductUnit.Kg,    40, 15),
            ("Sunflower Oil",     "Dry Goods", ProductUnit.Liter, 18, 6),
            ("Areni Wine",        "Beverage",  ProductUnit.Piece, 48, 12),
            ("Kilikia Beer",      "Beverage",  ProductUnit.Piece, 96, 24),
            ("Coffee Beans",      "Beverage",  ProductUnit.Kg,    7,  3),
        };
        foreach (var p in productSpec)
        {
            var product = new Product
            {
                RestaurantId = restaurant.Id,
                Name = p.Name,
                Category = p.Category,
                Unit = p.Unit,
                CurrentStock = p.Stock,
                LowStockThreshold = p.Low,
            };
            db.Products.Add(product);

            // Movement history: an initial stock-take, a delivery, and occasional wastage.
            var opening = Math.Round(p.Stock * 0.6m);
            db.Set<StockMovement>().Add(new StockMovement
            {
                RestaurantId = restaurant.Id, ProductId = product.Id, CreatedById = admin.Id,
                Type = StockMovementType.Initial, QuantityChange = opening, QuantityAfter = opening,
                Reason = "Opening stock-take", CreatedAt = now.AddDays(-30),
            });
            var delivery = p.Stock - opening;
            db.Set<StockMovement>().Add(new StockMovement
            {
                RestaurantId = restaurant.Id, ProductId = product.Id, CreatedById = admin.Id,
                Type = StockMovementType.Purchase, QuantityChange = delivery, QuantityAfter = p.Stock,
                Reason = "Supplier delivery", CreatedAt = now.AddDays(-rng.Next(3, 12)),
            });
        }

        // ── Orders + items + cash register ───────────────────────────────────
        // Cash rows are collected here, then snapshotted in chronological order
        // at the end so the drawer's running balance reads monotonically.
        var cashTxns = new List<CashRegisterTransaction>
        {
            new()
            {
                RestaurantId = restaurant.Id, CreatedById = admin.Id,
                Type = CashTransactionType.ManualIncome, Method = PaymentMethod.Cash,
                Amount = 50000m, Reason = "Opening cash float", CreatedAt = now.AddDays(-35),
            },
        };

        var occupiedTables = new HashSet<Guid>();
        // Rich 34-day history for trends, plus a busy "today"/"yesterday" so the
        // dashboard and reports (which default to today) open with live numbers.
        var orderDates = new List<DateTime>();
        for (int i = 0; i < 38; i++)
            orderDates.Add(now.AddDays(-rng.Next(3, 34)).Date.AddHours(11 + rng.Next(0, 12)).AddMinutes(rng.Next(0, 60)));
        var todayElapsed = (now - now.Date).TotalMinutes;
        for (int i = 0; i < 9; i++)
            orderDates.Add(now.Date.AddMinutes(rng.NextDouble() * todayElapsed));   // always today, always past
        for (int i = 0; i < 5; i++)
            orderDates.Add(now.AddDays(-1).Date.AddHours(12 + rng.Next(0, 10)).AddMinutes(rng.Next(0, 60)));
        orderDates = orderDates.OrderBy(d => d).ToList();

        foreach (var when in orderDates)
        {
            var table = tables[rng.Next(tables.Count)];
            var server = waiters[rng.Next(waiters.Count)];

            // Status mix: mostly paid, some still open, a few cancelled.
            var roll = rng.NextDouble();
            var status = roll < 0.72 ? OrderStatus.Paid
                       : roll < 0.90 ? OrderStatus.Open
                       : OrderStatus.Cancelled;
            // Open orders only make sense "now", not weeks ago — keep them recent.
            if (status == OrderStatus.Open && when < now.AddDays(-1)) status = OrderStatus.Paid;

            var order = new Order
            {
                RestaurantId = restaurant.Id,
                TableId = table.Id,
                CreatedById = server.Id,
                Status = status,
                CreatedAt = when,
            };

            var itemCount = 2 + rng.Next(4);
            var picks = Enumerable.Range(0, itemCount)
                .Select(_ => menuItems[rng.Next(menuItems.Count)])
                .DistinctBy(m => m.Id)
                .ToList();
            decimal total = 0;
            foreach (var mi in picks)
            {
                var qty = 1 + rng.Next(3);
                total += mi.Price * qty;
                order.Items.Add(new OrderItem
                {
                    RestaurantId = restaurant.Id,
                    OrderId = order.Id,
                    MenuItemId = mi.Id,
                    MenuItemName = mi.Name,
                    Price = mi.Price,
                    Quantity = qty,
                    Status = status == OrderStatus.Open
                        ? (OrderItemStatus)rng.Next(0, 4)
                        : OrderItemStatus.Served,
                    CreatedAt = when,
                });
            }

            if (status == OrderStatus.Paid)
            {
                var pmRoll = rng.NextDouble();
                var method = pmRoll < 0.50 ? PaymentMethod.Cash
                           : pmRoll < 0.83 ? PaymentMethod.Card
                           : pmRoll < 0.95 ? PaymentMethod.BankTransfer
                           : PaymentMethod.Other;
                order.PaymentMethod = method;
                // ~1 in 4 paid orders is tied to a known regular.
                if (rng.Next(4) == 0) order.ClientId = clients[rng.Next(clients.Count)].Id;

                cashTxns.Add(new CashRegisterTransaction
                {
                    RestaurantId = restaurant.Id, CreatedById = server.Id, OrderId = order.Id,
                    Type = CashTransactionType.OrderPayment, Method = method,
                    Amount = total, Reason = $"Order · Table {table.Number}", CreatedAt = when,
                });
            }
            else if (status == OrderStatus.Open)
            {
                occupiedTables.Add(table.Id);
            }

            db.Orders.Add(order);
        }

        // A couple of manual cash-outs near the present.
        cashTxns.Add(new CashRegisterTransaction
        {
            RestaurantId = restaurant.Id, CreatedById = admin.Id,
            Type = CashTransactionType.ManualExpense, Method = PaymentMethod.Cash,
            Amount = -18000m, Reason = "Produce market run", CreatedAt = now.AddDays(-2),
        });
        cashTxns.Add(new CashRegisterTransaction
        {
            RestaurantId = restaurant.Id, CreatedById = cashiers[0].Id,
            Type = CashTransactionType.ManualExpense, Method = PaymentMethod.Cash,
            Amount = -9500m, Reason = "Cleaning supplies", CreatedAt = now.AddDays(-1),
        });

        // Snapshot the running cash balance in true chronological order — only
        // Cash moves the drawer; Card/Transfer/Other rows carry the unchanged total.
        var runningCash = 0m;
        foreach (var txn in cashTxns.OrderBy(x => x.CreatedAt))
        {
            if (txn.Method == PaymentMethod.Cash) runningCash += txn.Amount;
            txn.BalanceAfter = runningCash;
        }
        restaurant.CashBalance = runningCash;
        db.Set<CashRegisterTransaction>().AddRange(cashTxns);

        // Open orders hold their tables.
        foreach (var t in tables.Where(t => occupiedTables.Contains(t.Id)))
            t.Status = TableStatus.Occupied;

        // ── Reservations (future confirmed + past history) ───────────────────
        // One per (table, day) to satisfy the booking rules; capacity-valid.
        var freeTables = tables.Where(t => t.Status == TableStatus.Free).ToList();
        var reservationSpec = new (int DayOffset, int Hour, int Duration, ReservationStatus Status)[]
        {
            (1, 19, 120, ReservationStatus.Confirmed),
            (1, 20, 120, ReservationStatus.Confirmed),
            (2, 18, 150, ReservationStatus.Confirmed),
            (2, 21, 120, ReservationStatus.Confirmed),
            (3, 19, 180, ReservationStatus.Confirmed),
            (4, 20, 120, ReservationStatus.Confirmed),
            (6, 19, 240, ReservationStatus.Confirmed),
            (-2, 20, 120, ReservationStatus.Completed),
            (-3, 19, 150, ReservationStatus.Completed),
            (-4, 21, 120, ReservationStatus.NoShow),
            (-5, 18, 120, ReservationStatus.Completed),
            (-7, 20, 180, ReservationStatus.Completed),
        };
        var firstNames = clientSpec.Select(c => c.Name).ToList();
        for (int i = 0; i < reservationSpec.Length; i++)
        {
            var spec = reservationSpec[i];
            var table = freeTables[i % freeTables.Count];
            var guestCount = Math.Min(table.Capacity, 2 + rng.Next(table.Capacity - 1));
            var startAt = now.AddDays(spec.DayOffset).Date.AddHours(spec.Hour);
            var linkClient = rng.Next(2) == 0 ? clients[rng.Next(clients.Count)] : null;
            db.Set<Reservation>().Add(new Reservation
            {
                RestaurantId = restaurant.Id,
                TableId = table.Id,
                CreatedById = waiters[rng.Next(waiters.Count)].Id,
                ClientId = linkClient?.Id,
                GuestName = linkClient?.FullName ?? firstNames[rng.Next(firstNames.Count)],
                GuestPhone = "+374 94 30 " + rng.Next(10, 99) + " " + rng.Next(10, 99),
                GuestCount = guestCount,
                StartAt = startAt,
                DurationMinutes = spec.Duration,
                Status = spec.Status,
                Notes = i % 4 == 0 ? "Window table requested" : null,
                CreatedAt = startAt.AddDays(-3),
            });
        }

        // ── Schedule / shifts (next 7 days) ──────────────────────────────────
        foreach (var emp in staff.Where(u => u.RoleId != Role("Admin").Id))
        {
            var shiftDays = Enumerable.Range(0, 7).OrderBy(_ => rng.Next()).Take(3 + rng.Next(2));
            foreach (var d in shiftDays)
            {
                var morning = rng.Next(2) == 0;
                var start = now.AddDays(d).Date.AddHours(morning ? 10 : 16);
                db.Set<Shift>().Add(new Shift
                {
                    RestaurantId = restaurant.Id,
                    UserId = emp.Id,
                    CreatedById = admin.Id,
                    StartAt = start,
                    EndAt = start.AddHours(8),
                    Status = ShiftStatus.Scheduled,
                });
            }
        }

        // ── A few activity-log entries so the audit page isn't blank ─────────
        var logs = new (ActivityCategory Cat, string Action, string Desc, int DayAgo)[]
        {
            (ActivityCategory.Settings, "Update", "Restaurant profile created", 35),
            (ActivityCategory.Menu, "Create", "Menu category 'Khorovats (BBQ)' added", 34),
            (ActivityCategory.Staff, "Create", "Staff member Narek Grigoryan added", 33),
            (ActivityCategory.Client, "Deposit", "Karine Sahakyan topped up 42,000 ֏", 30),
            (ActivityCategory.Inventory, "StockIn", "Supplier delivery recorded for Pork", 11),
            (ActivityCategory.Reservation, "Create", "Reservation confirmed for tomorrow", 4),
            (ActivityCategory.CashRegister, "Expense", "Manual cash-out: Produce market run", 2),
        };
        foreach (var l in logs)
        {
            db.Set<ActivityLogEntry>().Add(new ActivityLogEntry
            {
                RestaurantId = restaurant.Id,
                UserId = admin.Id,
                UserName = admin.FullName,
                Category = l.Cat,
                Action = l.Action,
                EntityType = l.Cat.ToString(),
                Description = l.Desc,
                CreatedAt = now.AddDays(-l.DayAgo),
            });
        }

        await db.SaveChangesAsync(ct);
        logger.LogInformation(
            "Demo seed complete: restaurant '{Name}', {Staff} staff, {Items} menu items, {Orders} orders. Login: {Email} / {Pwd}",
            restaurant.Name, staff.Count, menuItems.Count, orderDates.Count, AdminEmail, DemoPassword);
    }
}
