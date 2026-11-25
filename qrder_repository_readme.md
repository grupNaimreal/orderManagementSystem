# QRDER — Bean Crate Cafe Digital Order Management & POS System

**QRDER** is an ASP.NET Core MVC (`.NET 10`) web application built for **Bean Crate Cafe** (Antipolo City, Philippines) to replace manual paper order lists with a table-QR mobile ordering experience for customers and a real-time Point-of-Sale (POS) workspace for Staff and Owners.

---

## Key Features

### 1. Customer Mobile & Tablet Ordering
- **Table QR Auto-Entry (`/?table=Table%201`):** Scanning a table QR code pre-fills the customer's dining preference (`Dine-In` with table number or `Take-Out`), validates the customer's name (2–30 characters with profanity filtering), and creates a 10-minute active session.
- **101-Item Digital Menu Across 17 Categories:** Mirrors Bean Crate Cafe's physical 11-page menu sequence (starting with signature **Bean Crate Latte** under **Coffee**), complete with size/temperature variants, paid add-ons sorted cheapest-first, deferred image loading, and category artwork fallbacks.
- **Dual Payment Checkout:**
  - **Cash at Counter:** Sends the order straight to the POS queue for counter payment confirmation.
  - **Online Checkout (via Maya):** Integrates with **Maya Checkout v1 Sandbox** (`QRPh`, `Maya Wallet`, and `Debit/Credit Cards`) with strict `PAYMENT_SUCCESS` verification, live status polling, cancelled-checkout recovery, and one-tap switching to Cash.
- **Live Order Tracking (`My Orders` & `Order Details`):** Automatically updates status (`Pending` → `Preparing` → `Completed`) in real time and displays a clear completion notice when ready.

### 2. Staff & Owner POS Workspace
- **Live Overview & Order Board (`/Orders`):** Real-time order queue with KPI counters, status/payment filter tabs, instant search, payment confirmation (`Cash` or `Maya`), preparation progression, and recorded cancellation reasons.
- **Visual Counter Order Builder (`/Orders/CreateForCustomer`):** Lets staff select or create an active customer session and tap visual category/product cards with variant & add-on modals.
- **Customer Sessions Directory (`/Customers`):** Monitors active vs. expired 10-minute sessions with live background sync and direct `+ Order` shortcuts.
- **Menu Studio (`/Products`):** Role-gated product catalog management (`Owner` create/edit/availability toggles; `Employee` read-only lookup).
- **Sales Reports & Project Encord Bridge (`/Reports`):** Daily, weekly, monthly, and custom date-range reporting with Gross Sales, Net Sales, 12% VAT breakdown, Cash vs. Online totals, CSV export, and 15-column (`A–O`) clipboard rows formatted for **Project Encord**.
- **Owner Governance (`/Employees` & `/AuditLogs`):** Staff account provisioning and a chronological Audit Trail of orders, payments, cancellations, and report exports.
- **Printable Table QR Generator (`/Home/BusinessDetails`):** Generates and saves a persistent public App Base URL (`localStorage`) for printing table QR cards.

---

## Tech Stack

- **Backend:** C# / ASP.NET Core MVC (`.NET 10`), Razor Views, ASP.NET Core Identity (`Owner` & `Employee` roles)
- **Database:** Entity Framework Core 10 with **PostgreSQL** (`Npgsql`, hosted on **Neon**)
- **Payments:** Maya Checkout v1 Sandbox API (`Services/MayaCheckoutService.cs`)
- **Frontend:** Bootstrap 5, Vanilla JS, **Source Serif 4** Roastery Press editorial design system (`wwwroot/css/site.css`)
- **Hosting:** Multi-stage `.NET 10` Docker container (`Dockerfile`) deployed on **Render**

---

## Default Demo Credentials

| Role | Email | Password |
| :--- | :--- | :--- |
| **Owner** | `owner@beancrate.local` | `BeanCrateOwner2026!` |
| **Employee (Staff)** | `staff@beancrate.local` | `BeanCrateStaff2026!` |

*(Configure via environment variables `InitialOwner__Email`, `InitialOwner__Password`, `InitialEmployee__Email`, and `InitialEmployee__Password`.)*

---

## Local & Container Setup

1. **Configure Connection String:**
   ```bash
   dotnet user-secrets set "ConnectionStrings:DefaultConnection" "postgresql://user:password@host/dbname?sslmode=require"
   ```
2. **Apply Migrations & Run:**
   ```bash
   dotnet ef database update
   dotnet run
   ```
   On startup, `Program.cs` applies pending EF Core migrations, ensures the `AuditLogs` table exists, seeds all **101 Bean Crate Cafe menu items** (`MenuSeeder`), and provisions the `Owner` and `Employee` roles (`IdentitySeeder`).