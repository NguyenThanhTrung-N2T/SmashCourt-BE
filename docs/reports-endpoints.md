# Reports API — Endpoints & Req/Res

Base route: `/api/reports`

Auth: Bearer token required. Controller requires role `OWNER` or `BRANCH_MANAGER` unless an endpoint is Owner-only or Manager-only (see each endpoint).

Common query DTO: `ReportFilterDto` (query string)
- `FromDate` (DateOnly?, format `YYYY-MM-DD`) — default: 30 days before
- `ToDate` (DateOnly?, format `YYYY-MM-DD`) — default: today
- `BranchId` (Guid?) — OWNER may supply; BRANCH_MANAGER uses their branch
- `GroupBy` (string?) — allowed: `day`, `week`, `month`, `branch`, `courtType`, `paymentMethod`, `hour`, `dayOfWeek`

All responses are wrapped in `ApiResponse<T>` with the usual `success`, `data`, `message` shape.

---

**GET /api/reports/dashboard/owner**
- Auth: Owner only (`AuthorizationPolicies.OwnerOnly`)
- Query: `ReportFilterDto`
- Response: `ApiResponse<OwnerDashboardDto>`
- Top-level fields in `OwnerDashboardDto`:
  - `summary`: `DashboardSummaryDto` (totalRevenue, totalBookings, completedBookings, cancelledBookings, noShowBookings, newCustomers, occupancyRate, onlinePaymentRevenue, cashPaymentRevenue)
  - `topBranches`: `TopBranchDto[]` (branchId, branchName, revenue, bookingCount)
  - `topCustomers`: `TopCustomerDto[]` (customerId, fullName, totalRevenue, bookingCount, loyaltyTier)
  - `revenueTrend`: `RevenueTrendDto[]` (period, revenue, bookingCount)
  - `bookingTrend`: `BookingTrendDto[]` (period, totalCount, completedCount)

**GET /api/reports/dashboard/manager**
- Auth: Manager only (`AuthorizationPolicies.ManagerOnly`)
- Query: `ReportFilterDto` (branch is the manager's branch)
- Response: `ApiResponse<ManagerDashboardDto>` (same structure as owner `summary`, `topCustomers`, `revenueTrend`, `bookingTrend`)

**GET /api/reports/revenue**
- Auth: Owner or Manager
- Query: `ReportFilterDto`
- Response: `ApiResponse<RevenueReportDto>`
- `RevenueReportDto` fields:
  - `totalRevenue` (decimal)
  - `courtRevenue` (decimal)
  - `serviceRevenue` (decimal)
  - `discountAmount` (decimal)
  - `averageBookingValue` (decimal)
  - `items`: `RevenueItemDto[]` (`period` string, `revenue` decimal, `bookingCount` int)

**GET /api/reports/bookings**
- Auth: Owner or Manager
- Query: `ReportFilterDto`
- Response: `ApiResponse<BookingReportDto>`
- `BookingReportDto` fields:
  - `totalBookings`, `completed`, `cancelled`, `noShow`, `pendingPayment`, `onlineBookings`, `walkInBookings` (ints)
  - `cancellationRate`, `noShowRate` (decimal)
  - `items`: `BookingItemDto[]` (`period` string, `bookingCount` int, `completedCount` int, `cancelledCount` int)

**GET /api/reports/courts/utilization**
- Auth: Owner or Manager
- Query: `ReportFilterDto`
- Response: `ApiResponse<CourtUtilizationReportDto>`
- `CourtUtilizationReportDto` fields:
  - `overallOccupancyRate`, `totalAvailableHours`, `totalBookedHours` (decimal)
  - `peakHours`: `PeakHourDto[]` (`hour` int, `bookingCount` int, `occupancyRate` decimal)
  - `offPeakHours`: `PeakHourDto[]`
  - `topCourts` / `items`: `CourtUtilizationItemDto[]` (courtId, courtName, period, bookedHours, availableHours, occupancyRate)

**GET /api/reports/customers**
- Auth: Owner or Manager
- Query: `ReportFilterDto`
- Response: `ApiResponse<CustomerStatisticsReportDto>`
- `CustomerStatisticsReportDto` fields:
  - `totalCustomers`, `newCustomers`, `repeatCustomers` (int)
  - `repeatCustomerRate`, `averageBookingsPerCustomer`, `averageRevenuePerCustomer` (decimal)
  - `loyaltyTierDistribution`: `LoyaltyTierDistributionDto[]` (tierName, customerCount, percentage)
  - `acquisitionTrend`: `CustomerAcquisitionTrendDto[]` (period, newCustomers)

**GET /api/reports/customers/top-spenders**
- Auth: Owner or Manager
- Query: `ReportFilterDto`, plus `page` (int, default 1), `pageSize` (int, default 20)
- Response: `ApiResponse<TopSpendersReportDto>`
- `TopSpendersReportDto`:
  - `totalCount`, `page`, `pageSize` (int)
  - `items`: `TopSpenderDto[]` (customerId, fullName, email, phone, totalRevenue, bookingCount, loyaltyTier)

**GET /api/reports/services**
- Auth: Owner or Manager
- Query: `ReportFilterDto`
- Response: `ApiResponse<ServicePerformanceReportDto>`
- `ServicePerformanceReportDto` fields: `totalServiceRevenue` (decimal), `totalBookingsWithServices` (int), `serviceAttachmentRate` (decimal), `averageServiceRevenuePerBooking` (decimal), `topServices` (`ServiceItemDto[]`), `serviceTrend` (`ServiceTrendDto[]`)

**GET /api/reports/promotions**
- Auth: Owner or Manager
- Query: `ReportFilterDto`
- Response: `ApiResponse<PromotionEffectivenessReportDto>`
- `PromotionEffectivenessReportDto` fields: `totalDiscountAmount` (decimal), `totalPromotionUsage` (int), `averageDiscountPerUsage` (decimal), `promotionConversionRate` (decimal), `topPromotions` (`PromotionItemDto[]`), `promotionTrend` (`PromotionTrendDto[]`)

---

Quick examples

- Example request header:

```
Authorization: Bearer <JWT>
Accept: application/json
```

- Example query URL (daily revenue):

```
GET /api/reports/revenue?fromDate=2026-05-01&toDate=2026-05-27&groupBy=day
```

- Minimal `ApiResponse<T>` skeleton:

```json
{
  "success": true,
  "data": { /* DTO payload as documented above */ },
  "message": "ok"
}
```

Files referenced (source):
- [Controllers/ReportController.cs](Controllers/ReportController.cs#L1)
- [DTOs/Report/ReportFilterDto.cs](DTOs/Report/ReportFilterDto.cs#L1)
- [DTOs/Report/OwnerDashboardDto.cs](DTOs/Report/OwnerDashboardDto.cs#L1)

Below are concise example requests and example `ApiResponse<T>` responses for each endpoint. Values are illustrative — frontend can rely on property names and types.

---

## Examples

1) Dashboard — Owner

Request:

```
GET /api/reports/dashboard/owner?fromDate=2026-05-01&toDate=2026-05-27
Authorization: Bearer <JWT>
```

Response (ApiResponse<OwnerDashboardDto>):

```json
{
  "success": true,
  "data": {
    "summary": {
      "totalRevenue": 12345.67,
      "totalBookings": 250,
      "completedBookings": 200,
      "cancelledBookings": 30,
      "noShowBookings": 20,
      "newCustomers": 50,
      "occupancyRate": 0.72,
      "onlinePaymentRevenue": 7000.00,
      "cashPaymentRevenue": 5345.67
    },
    "topBranches": [ { "branchId": "00000000-0000-0000-0000-000000000001", "branchName": "Central", "revenue": 5000.0, "bookingCount": 80 } ],
    "topCustomers": [ { "customerId": "00000000-0000-0000-0000-0000000000aa", "fullName": "Nguyen A", "totalRevenue": 450.0, "bookingCount": 5, "loyaltyTier": "Gold" } ],
    "revenueTrend": [ { "period": "2026-05-01", "revenue": 400.0, "bookingCount": 10 } ],
    "bookingTrend": [ { "period": "2026-05-01", "totalCount": 10, "completedCount": 8 } ]
  },
  "message": "Lấy dashboard thành công"
}
```

2) Dashboard — Manager

Request:

```
GET /api/reports/dashboard/manager?fromDate=2026-05-01&toDate=2026-05-27
Authorization: Bearer <JWT>
```

Response (ApiResponse<ManagerDashboardDto>): same shape as Owner but scoped to one branch.

3) Revenue Report

Request:

```
GET /api/reports/revenue?fromDate=2026-05-01&toDate=2026-05-27&groupBy=day
Authorization: Bearer <JWT>
```

Response (ApiResponse<RevenueReportDto>):

```json
{
  "success": true,
  "data": {
    "totalRevenue": 12345.67,
    "courtRevenue": 9000.00,
    "serviceRevenue": 2345.67,
    "discountAmount": 100.00,
    "averageBookingValue": 49.38,
    "items": [ { "period": "2026-05-01", "revenue": 400.0, "bookingCount": 10 } ]
  },
  "message": "ok"
}
```

4) Booking Report

Request:

```
GET /api/reports/bookings?fromDate=2026-05-01&toDate=2026-05-27&groupBy=day
Authorization: Bearer <JWT>
```

Response (ApiResponse<BookingReportDto>):

```json
{
  "success": true,
  "data": {
    "totalBookings": 250,
    "completed": 200,
    "cancelled": 30,
    "noShow": 20,
    "pendingPayment": 5,
    "onlineBookings": 150,
    "walkInBookings": 100,
    "cancellationRate": 0.12,
    "noShowRate": 0.08,
    "items": [ { "period": "2026-05-01", "bookingCount": 10, "completedCount": 8, "cancelledCount": 1 } ]
  },
  "message": "ok"
}
```

5) Court Utilization

Request:

```
GET /api/reports/courts/utilization?fromDate=2026-05-01&toDate=2026-05-27
Authorization: Bearer <JWT>
```

Response (ApiResponse<CourtUtilizationReportDto>):

```json
{
  "success": true,
  "data": {
    "overallOccupancyRate": 0.65,
    "totalAvailableHours": 1000.0,
    "totalBookedHours": 650.0,
    "peakHours": [ { "hour": 18, "bookingCount": 40, "occupancyRate": 0.9 } ],
    "offPeakHours": [ { "hour": 10, "bookingCount": 5, "occupancyRate": 0.1 } ],
    "topCourts": [ { "courtId": "00000000-0000-0000-0000-000000000010", "courtName": "Court 1", "period": "2026-05-01", "bookedHours": 120.0, "availableHours": 150.0, "occupancyRate": 0.8 } ],
    "items": []
  },
  "message": "ok"
}
```

6) Customer Statistics

Request:

```
GET /api/reports/customers?fromDate=2026-05-01&toDate=2026-05-27
Authorization: Bearer <JWT>
```

Response (ApiResponse<CustomerStatisticsReportDto>):

```json
{
  "success": true,
  "data": {
    "totalCustomers": 1200,
    "newCustomers": 50,
    "repeatCustomers": 300,
    "repeatCustomerRate": 0.25,
    "averageBookingsPerCustomer": 1.4,
    "averageRevenuePerCustomer": 45.5,
    "loyaltyTierDistribution": [ { "tierName": "Gold", "customerCount": 100, "percentage": 0.083 } ],
    "acquisitionTrend": [ { "period": "2026-05-01", "newCustomers": 2 } ]
  },
  "message": "ok"
}
```

7) Top Spenders (paginated)

Request:

```
GET /api/reports/customers/top-spenders?page=1&pageSize=20&fromDate=2026-05-01
Authorization: Bearer <JWT>
```

Response (ApiResponse<TopSpendersReportDto>):

```json
{
  "success": true,
  "data": {
    "totalCount": 50,
    "page": 1,
    "pageSize": 20,
    "items": [ { "customerId": "00000000-0000-0000-0000-00000000cc01", "fullName": "Tran B", "email": "tranb@example.com", "phone": "0123456789", "totalRevenue": 1200.0, "bookingCount": 12, "loyaltyTier": "Platinum" } ]
  },
  "message": "ok"
}
```

8) Service Performance

Request:

```
GET /api/reports/services?fromDate=2026-05-01&toDate=2026-05-27
Authorization: Bearer <JWT>
```

Response (ApiResponse<ServicePerformanceReportDto>):

```json
{
  "success": true,
  "data": {
    "totalServiceRevenue": 2345.67,
    "totalBookingsWithServices": 80,
    "serviceAttachmentRate": 0.32,
    "averageServiceRevenuePerBooking": 29.32,
    "topServices": [ { "serviceId": "00000000-0000-0000-0000-00000000s001", "serviceName": "Ball Rental", "revenue": 500.0, "bookingCount": 50, "averageRevenue": 10.0 } ],
    "serviceTrend": [ { "period": "2026-05-01", "serviceRevenue": 40.0, "bookingCount": 2 } ]
  },
  "message": "ok"
}
```

9) Promotion Effectiveness

Request:

```
GET /api/reports/promotions?fromDate=2026-05-01&toDate=2026-05-27
Authorization: Bearer <JWT>
```

Response (ApiResponse<PromotionEffectivenessReportDto>):

```json
{
  "success": true,
  "data": {
    "totalDiscountAmount": 500.0,
    "totalPromotionUsage": 120,
    "averageDiscountPerUsage": 4.17,
    "promotionConversionRate": 0.05,
    "topPromotions": [ { "promotionId": "00000000-0000-0000-0000-0000000p001", "promotionName": "May Sale", "promotionCode": "MAY20", "usageCount": 60, "totalDiscount": 300.0, "revenueAfterDiscount": 1200.0, "averageDiscount": 5.0 } ],
    "promotionTrend": [ { "period": "2026-05-01", "usageCount": 3, "totalDiscount": 15.0 } ]
  },
  "message": "ok"
}
```

---
