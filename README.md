# QRDER — Bean Crate Cafe Digital Order Management & POS System

**Live Deployment:** [https://qrder-beancrate.onrender.com](https://qrder-beancrate.onrender.com)  
**Health Check Endpoint:** `GET /health`

**QRDER** is a mobile-first web ordering and Neo-Industrial Point-of-Sale (POS) system built for **Bean Crate Cafe** (Sen. Lorenzo Sumulong Memorial Circle, Brgy. San Jose, Antipolo City). It replaces handwritten paper *"lista"* order slips with direct QR table self-ordering for customers and a real-time counter/kitchen workspace for Staff and the Owner.

---

## Core Modules & Features

### 1. Customer QR Ordering (Mobile-First, Light Mode)
* **Zero-Scroll Session Onboarding (`/`):** Customers select **Dine-In** or **Take-Out**, enter an optional **Table #**, and start a 10-minute sliding session validated against profanity and invalid characters (`CustomerNameValidator`).
* **Digital Menu (`/Products`):** 17 categories and 102 menu items (including *Bean Crate Latte* featured first in *Coffee* and *The Cookie Pour* staged in *Desserts & Banana Pudding*) with interactive size/temperature variants, add-ons, category quick-jump cards, and instant search.
* **Cart, Checkout & Live Tracking (`/Orders/Create`, `/Orders/Details/{id}`, `/Orders/MyOrders`):** Supports **Cash at Counter** and **Online Checkout (via Maya Sandbox)** with server-side price calculation, real-time order status polling, cancellation/failure recovery, direct switch-to-cash, and a collapsible 80mm **Order Summary Slip**.

### 2. Order Board & Counter POS (`Owner`, `Employee`)
* **Overview & Telemetry (`/Home/Index`):** Live counter shortcuts and daily operational snapshot (Orders Today, Active Queue, Unpaid/Pending, Completed Sales).
* **Order Board (`/Orders/Index`):** Live queue telemetry bar, Split-Flap KPI counters, status/payment filter pills (`All`, `Pending`, `Preparing`, `Unpaid`, `Completed`, `Cancelled`), instant search, and 10-second background synchronization.
* **Counter Walk-In Orders (`/Orders/CreateForCustomer`):** Staff can create and manage orders on behalf of walk-in or seated customers.
* **Payment & Status Controls (`/Orders/Details/{id}`):** Staff verify Cash or Maya payments before advancing orders to `Preparing` and `Completed`, with printable non-BIR Order Summary Slips.

### 3. Menu Studio (`/Products`)
* **Owner (`Owner` role):** Full CRUD for products, categories, pricing, variants, add-ons, product images, and item availability.
* **Staff (`Employee` role):** Read-only access to inspect menu items, pricing, and customization options.

### 4. Customer Sessions (`/Customers`)
* Live directory of active and expired/ended customer QR sessions sorted newest-first with Philippine Standard Time (`Asia/Manila`, UTC+8) timestamps, automatic 10-minute inactivity expiration, and 10-second background sync.

### 5. Sales Reports & BIR/Encord Export (`/Reports`)
* Preset (`Today`, `Last 7 Days`, `This Month`) and custom date filters with **Gross Sales**, **Net Sales (Excl. VAT)**, **12% VAT**, **Cash vs. Maya** breakdown, **Top-Selling Items**, **CSV Export**, and one-click **15-column (`A–O`) clipboard copy** formatted for Project Encord workbooks.

### 6. Staff Accounts & Audit Trail (`Owner` Only)
* **Staff Accounts (`/Employees`):** Create, edit, reset passwords, and deactivate Owner or Employee accounts with employee codes.
* **Audit Trail (`/AuditLogs`):** Chronological governance log recording payment confirmations, report exports, and administrative actions in PHT.

---

## Technology Stack

* **Backend:** ASP.NET Core 10.0 (`net10.0`) MVC & Razor Pages (C#)
* **Database & ORM:** Entity Framework Core 10 with **PostgreSQL** (Neon/Render production) and automatic **SQLite** (`app.db`) fallback for local development
* **Authentication & Authorization:** ASP.NET Core Identity with Role-Based Access Control (`Owner`, `Employee`) and session-isolated customer tracking (`CustomerSessionService`)
* **Online Payment Integration:** Maya Checkout API (`MayaCheckoutService`) with strict server-side `PAYMENT_SUCCESS` verification
* **Background Worker:** `UnpaidOrderExpiryService` (automatically cancels unpaid online orders after 10 minutes)
* **Frontend:** Bootstrap 5, custom **Source Serif 4** Neo-Industrial Kitchen Telemetry & Split-Flap UI (`wwwroot/css/site.css`), responsive across Mobile, Tablet Landscape, and PC, with Light/Dark Mode support for Staff/Owner POS terminals
* **Deployment:** Docker container on **Render** with `GET /health` keep-alive monitoring via **UptimeRobot**

---

## Security & Pentest Hardening

* **CSRF Protection:** `[ValidateAntiForgeryToken]` enforced across every state-changing `POST` action.
* **RBAC & IDOR Isolation:** Strict `[Authorize(Roles = ...)]` enforcement on Owner/Employee controllers and session-scoped order lookup preventing cross-customer order access.
* **XSS & Input Sanitization:** Razor HTML auto-encoding across all views, `JsonSerializer.Serialize` for embedded client data, and server-side display name validation/profanity filtering.
* **HTTP Security Headers & Cookies:** Enforces `X-Content-Type-Options: nosniff`, `X-Frame-Options: SAMEORIGIN`, `Referrer-Policy`, `Permissions-Policy`, `HSTS`, and `HttpOnly` / `SameSite=Lax` session cookies.

---

## Local Setup & Running

### Prerequisites
* [.NET 10 SDK](https://dotnet.microsoft.com/download)

### Run Locally (SQLite)
```bash
git clone <your-repository-url>
cd orderManagementSystem
dotnet restore
dotnet ef database update
dotnet run
```
On first startup, the application automatically creates `app.db`, applies pending EF Core migrations, seeds the `Owner` and `Employee` roles, creates default demo accounts, and populates the 102-item Bean Crate Cafe menu.

---

## Default Seeded Accounts

| Role | Email | Default Password |
| :--- | :--- | :--- |
| **Owner** | `owner@beancrate.local` | `BeanCrateOwner2026!` |
| **Staff / Employee** | `staff@beancrate.local` | `BeanCrateStaff2026!` |

> **Security Note:** Change default passwords after deployment via **Staff Accounts → Password**.

---

## Production Environment Variables (Render)

Configure the following environment variables in your Render Web Service dashboard:

| Variable | Description |
| :--- | :--- |
| `ASPNETCORE_ENVIRONMENT` | `Production` |
| `ConnectionStrings__DefaultConnection` | PostgreSQL connection string (or `DATABASE_URL` from Neon) |
| `Maya__BaseUrl` | `https://pg-sandbox.paymaya.com` (or production Maya endpoint) |
| `Maya__PublicApiKey` | Maya Checkout Public API Key |
| `Maya__SecretApiKey` | Maya Checkout Secret API Key |

---

## Project Team (Section BFIS02 — Group Kahit Alin)

* **Seth Rubio** — Project Leader
* **Jairo Diaz**
* **Julyanna Satiada**
* **Alexa Rayo**