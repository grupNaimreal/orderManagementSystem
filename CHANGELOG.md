# Changelog

All notable changes to **QRDER — Bean Crate Cafe Digital Order Management & POS System** are documented in this file.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and [Semantic Versioning](https://semver.org/).

---

## [1.0.0] — 2026-11-25

First production-ready release deployed at [https://qrder-beancrate.onrender.com](https://qrder-beancrate.onrender.com).

### Added
* **Customer QR Session Onboarding (`/`):**
  * Zero-scroll mobile-first session entry supporting **Dine-In** (with optional Table #), **Take-Out**, and display name validation with profanity/character filtering (`CustomerNameValidator`).
  * 10-minute sliding inactivity timeout with in-session name/dining preference editing and session termination.
* **Digital Menu & Item Customization (`/Products`):**
  * Seeded **102 Bean Crate Cafe items** across **17 categories** (`MenuSeeder`), featuring *Bean Crate Latte* first in *Coffee* and *The Cookie Pour* staged in *Desserts & Banana Pudding*.
  * Interactive size/temperature variants, paid add-ons, category quick-jump cards, instant search, and availability badges.
* **Cart, Checkout & Live Order Tracking (`/Orders/Create`, `/Orders/Details/{id}`, `/Orders/MyOrders`):**
  * Dual payment workflows supporting **Cash at Counter** and **Online Checkout (via Maya Sandbox)** (`MayaCheckoutService`) with server-side `PAYMENT_SUCCESS` verification, automatic decline/expiry recovery, and one-tap switch-to-cash.
  * Background worker (`UnpaidOrderExpiryService`) that automatically cancels unpaid online orders after 10 minutes.
  * Real-time order status polling, customer safe-to-leave confirmation banner, and a collapsible 80mm printable **Order Summary Slip** (non-BIR).
* **Neo-Industrial POS Workspace (`Owner` & `Employee`):**
  * **Overview (`/Home/Index`):** Live telemetry banner, counter quick actions, and daily operational snapshot.
  * **Order Board (`/Orders/Index`):** Split-Flap KPI tiles, status/payment filter pills, instant search, 10-second background sync, and counter walk-in ordering (`/Orders/CreateForCustomer`).
  * **Menu Studio (`/Products`):** Owner CRUD for products, options, pricing, images, and availability; read-only inspection for Employees.
  * **Customer Sessions (`/Customers`):** Newest-first session directory in Philippine Standard Time (`Asia/Manila`, UTC+8) with live status filters and 10-second sync.
  * **Sales Reports & Project Encord Integration (`/Reports`):** Date-range filters (`Today`, `Last 7 Days`, `This Month`, custom), Gross/Net/12% VAT breakdown, Cash vs. Maya totals, CSV export, and one-click **15-column (`A–O`) clipboard export** formatted for Project Encord workbooks.
  * **Staff Accounts & Audit Trail (`Owner` only — `/Employees`, `/AuditLogs`):** Staff account lifecycle management (create, edit, password reset, deactivate with lockout) and chronological governance audit logging (`AuditLogService`).
* **Deployment & Uptime Monitoring:**
  * Dockerized ASP.NET Core 10 runtime configured for **Render** with PostgreSQL (`DATABASE_URL`) and local SQLite fallback, plus `GET /health` endpoint monitored every 10 minutes via **UptimeRobot**.

### Changed
* **Unified Typography & Neo-Industrial Design System (`wwwroot/css/site.css`):** Standardized on **Source Serif 4** with tabular numerals, tactile press-down borders, brand-colored social hover states (Facebook, Instagram, TikTok), and branded telemetry loading curtains (`wwwroot/js/site.js`).
* **Theme Isolation:** Locked all customer-facing pages to **Light Mode** while enabling persistent **Light / Dark Mode** toggling across all Staff/Owner POS views.
* **Custom Brand Identity & Auth Gate:** Added updated `qrder-logo.png` favicon/brand marks and replaced the default Identity login screen with a custom Neo-Industrial POS Access Gate (`Areas/Identity/Pages/Account/Login.cshtml`).

### Fixed
* Resolved Maya checkout false-positive payment confirmations by verifying provider `PAYMENT_SUCCESS` status before marking orders `Paid` or unlocking `Preparing`.
* Fixed `MenuSeeder.ResolveProductImageUrl` parameter order so all product images resolve reliably on Linux/Docker containers.
* Enabled Staff (`Employee`) access to `/Customers/Create` for walk-in counter sessions.
* Fixed mobile checkout card text wrapping, redundant sticky cart item counters, and `ForwardedHeadersOptions.KnownIPNetworks` .NET 10 deprecation warning.

### Security
* Enforced `[ValidateAntiForgeryToken]` across all 25 state-changing `POST` endpoints.
* Verified Role-Based Access Control (`[Authorize(Roles = "Owner")]` / `[Authorize(Roles = "Owner,Employee")]`) and session-scoped IDOR isolation on customer orders.
* Hardened HTTP response headers (`X-Content-Type-Options: nosniff`, `X-Frame-Options: SAMEORIGIN`, `Referrer-Policy`, `Permissions-Policy`, `HSTS`) and `HttpOnly` / `SameSite=Lax` session cookies.

---

## [0.1.0 – 0.9.0] — Pre-Release Milestones (Oct–Nov 2026)
* **v0.1.0:** Initial ASP.NET Core MVC project scaffold, SQLite database, and ASP.NET Core Identity setup.
* **v0.3.0:** Core CRUD modules for Staff Accounts, Menu Products, and Counter Orders.
* **v0.6.0:** Customer QR session tracking, shopping cart, Maya Sandbox checkout service, and automated 10-minute unpaid order expiry worker.
* **v0.8.0:** Sales Reports with 12% VAT computation, CSV export, Project Encord batch copy, and Owner Audit Trail.