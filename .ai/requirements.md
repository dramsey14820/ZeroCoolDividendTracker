# Business Requirements Brief — ZeroCoolDividendTracker (v1)

> **Stage**: 1 (Business Requirements Brief)
> **Status**: Approved

## 1. Problem Statement & Target Users

**ZeroCoolDividendTracker** is a personal dividend-investing application for individual investors. Users can create and manage one or more portfolios of investments—both actual purchased holdings and modelled/watchlist portfolios—and track the dividends those investments produce over time.

The core goal is to give individual dividend investors clear visibility into dividend income, yield, reinvestment (DRIP), and investment performance so they can decide what positions to add, hold, or remove.

---

## 2. Primary Workflows (v1)

1. **Portfolio, Account & Holdings Management**
   - Create, edit, and delete **Portfolios** (supporting both *Actual* and *Modelled* portfolios) and **Accounts** (e.g., Taxable, IRA, Roth IRA).
   - Manage **Securities / Tickers** (`symbol`, `name`, `InvestmentType`, `IndustryType`, `SectorType`) along with user-maintained lookup classifications (`InvestmentTypes`, `IndustryTypes`, `SectorTypes`).
   - Record **Market Buys / Positions** (purchase date, number of shares including fractional shares, price per share) and track current holdings per portfolio/account.
2. **Dividend Payout Logging & Importing**
   - Log manual dividend payouts (`date`, `amount`, `investment` / `holding`, optional notes).
   - Import historical dividend payouts from the configured external market data source for tracked tickers.
3. **DRIP (Dividend Reinvestment Plan) Tracking**
   - Mark holdings or individual dividend payouts as reinvested (DRIP).
   - Record DRIP-acquired fractional shares and purchase price so share count, cost basis, and compounding dividend income stay accurate over time.
4. **Yield & Income Analytics**
   - View portfolio and per-holding metrics: total market value, total cost basis, unrealized gain/loss, annual & monthly dividend income (historical and projected), **Current Yield**, **Yield on Cost (YoC)**, and allocation breakdowns by **Sector**, **Industry**, and **Investment Type**.
   - Inspect dividend income history over time to evaluate investment performance and inform buy/sell decisions.
5. **Live / External Market Data Fetching**
   - Refresh latest stock/ETF quotes (price, basic metadata) and historical dividend payouts from a free external market data source.
   - Cache fetched quotes and dividend schedules locally in SQLite so the app stays responsive, respects free-tier rate limits, and remains usable offline.
6. **JSON Import & Export**
   - Export portfolios and all related data (accounts, classifications, securities, buys, dividends, DRIP records) to a portable JSON file.
   - Import a previously exported JSON file to restore backups or transfer data between devices/platforms.

---

## 3. Platform Targets & Architecture

All three targets are required in v1, sharing UI components via a single Razor Class Library (`*.Shared.UI`) built with pure CSS Grid/Flexbox and `.razor.css` isolation:

| Target | Host Project | Persistence | Packaging / Notes |
| :--- | :--- | :--- | :--- |
| **Web** | Blazor Server (`*.Web`) | Local SQLite DB | Local/self-hosted web app; no auth in v1 |
| **Windows Desktop** | .NET MAUI Blazor Hybrid (`*.Maui`) | Local SQLite DB in `%LOCALAPPDATA%` | Unpackaged (`WindowsPackageType=None`) + **WiX MSI** installer |
| **Android** | .NET MAUI Blazor Hybrid (`*.Maui`) | Local SQLite DB in `FileSystem.AppDataDirectory` | Mobile-first responsive CSS |

- **No cross-platform data sync in v1**: Each app installation persists independently to its own local SQLite database. Manual transfer is handled via JSON export/import.
- **No authentication in v1**: Single-user local experience.
- **Tech Stack**: `.NET 10+` LTS, explicit `public class Program` + `static void Main(string[] args)`, SQLite (`Microsoft.Data.Sqlite` + Dapper parameterized SQL or EF Core SQLite), xUnit + FluentAssertions + NSubstitute + bUnit.

---

## 4. Core Data Entities

Modernized from the user's legacy SQLite schema (`investments`, `investmentTypes`, `industryTypes`, `sectorTypes`, `marketBuys`, `dividends`) with proper relational integrity, ISO-8601 dates, and `decimal`-safe quantities/amounts (supporting fractional shares and exact currency math):

- **Portfolio**: `Id`, `Name`, `Description`, `IsModelled`, `CreatedUtc`
- **Account**: `Id`, `PortfolioId`, `Name`, `AccountType` (e.g., Taxable, IRA, Roth, Model)
- **Lookup Taxonomies**:
  - `InvestmentType` (`Id`, `Name` — e.g., Stock, ETF, REIT, BDC)
  - `IndustryType` (`Id`, `Name`)
  - `SectorType` (`Id`, `Name`)
- **Security / Ticker** *(legacy `investments`)*: `Id`, `Symbol`, `Name`, `InvestmentTypeId`, `IndustryTypeId`, `SectorTypeId`, `LastPrice`, `LastPriceUpdatedUtc`
- **Holding / Position**: Derived or tracked per `(PortfolioId, AccountId, SecurityId)` with `IsDripEnabled`
- **MarketBuy** *(legacy `marketBuys`)*: `Id`, `HoldingId` (or `AccountId` + `SecurityId`), `NumberOfShares` (`decimal`), `PricePerShare` (`decimal`), `PurchaseDate`, `IsDrip` (links to `DividendPaymentId` when generated by DRIP)
- **DividendPayment** *(legacy `dividends`)*: `Id`, `HoldingId` (or `AccountId` + `SecurityId`), `PaymentDate`, `ExDividendDate` (nullable), `Amount` (`decimal`), `IsReinvested`

*(Exact SQLite DDL, constraints, and indexes will be defined in Stage 2 slice specs.)*

---

## 5. Free External Market Data Sources (Research Summary)

| Source | Cost / API Key | Rate Limits | Quotes | Dividend History | Sector/Industry | Notes |
| :--- | :--- | :--- | :--- | :--- | :--- | :--- |
| **Yahoo Finance** *(unofficial chart/quote endpoints)* | Free, **no API key** | Best-effort (polite throttling + local cache) | Yes | Yes (full history) | Limited/varies | Zero-friction default for personal apps; unofficial so endpoints can change |
| **Finnhub** | Free API key | ~60 calls/min | Yes (real-time US) | Basic dividends | Basic profile | Official, stable, generous rate limit |
| **Financial Modeling Prep (FMP)** | Free API key | ~250 calls/day | Yes | Yes (limited depth on free) | Yes (profile) | Good balance of quotes + fundamentals + dividends |
| **Tiingo** | Free API key | Generous EOD free tier | EOD / limited live | Yes (distributions) | No | Reliable official EOD + dividend adjustments |
| **Alpha Vantage** | Free API key | 25 calls/day | Yes | Yes | Overview endpoint | Too tight for refreshing medium portfolios daily |

**Approved v1 Design**:
- Define a pluggable `IMarketDataProvider` interface behind local SQLite caching.
- Ship with **Yahoo Finance** (direct `HttpClient`, zero API key) as the out-of-the-box default provider, plus **Finnhub** and **Financial Modeling Prep (FMP)** as user-configurable official free-key providers in Settings.

---

## 6. Explicit Out-of-Scope (v1)

- Brokerage API syncing (Plaid, Schwab, Fidelity, etc.)
- Tax-lot accounting / wash-sale reporting / capital gains tax exports
- Multi-currency conversion (v1 assumes a single currency, e.g., USD)
- User authentication, login, or role-based access control (RBAC)
- Cloud sync or shared database across Web, Windows Desktop, and Android

