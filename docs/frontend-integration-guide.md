# Frontend Integration Guide — Courts API Refactor

The Courts API has been refactored to support production-grade dashboards, pagination, and date-scoped views. This guide outlines the breaking changes and new endpoints.

## 1. Split Management Dashboard

The monolithic `GET /api/courts/management-dashboard` endpoint is **DELETED**. It is replaced by two specialized endpoints to allow independent polling and pagination.

### 1a. Stats Endpoint (Pollable)
Retrieve only the summary stats (Ready, Booked, Playing, Suspended).
- **Endpoint:** `GET /api/courts/management-dashboard/stats`
- **Params:** `?branchId` (optional), `?date` (yyyy-MM-dd, defaults to today)
- **Use case:** Poll this every 30-60s to update the header counters without reloading the court cards.

### 1b. Court Cards Endpoint (Paginated)
Retrieve the list of court cards with their mini-timelines.
- **Endpoint:** `GET /api/courts/management-dashboard/courts`
- **Params:** `?branchId`, `?date`, `?search`, `?typeId`, `?page`, `?pageSize`
- **Pagination:** Returns `PagedResult<CourtManagementCardDto>`.
- **Note:** `bookingsTodayCount` is renamed to `bookingsCount`.

## 2. Dedicated Timeline View

New endpoint for the full timeline "Calendar" view, providing booking identity (who is playing?).
- **Endpoint:** `GET /api/courts/management-timeline`
- **Params:** `?branchId`, `?date` (required), `?typeId`
- **Data Shape:** `CourtManagementTimelineDto`
    - `operatingHours`: `{ open, close }` (HH:mm)
    - `slots`: includes `bookingId`, `playerName`, `bookingStatus`.

## 3. Date-Aware Detail Modal

The court details (prices, current player, upcoming slots) now support browsing other dates.
- **Endpoint:** `GET /api/courts/{id}/management-details`
- **Params:** `?date` (yyyy-MM-dd, defaults to today)
- **Logic:**
    - If `date` is today: `currentPlayer` shows who's active *now*. `upcomingBookings` shows slots *after now*.
    - If `date` is not today: `currentPlayer` is always `null`. `upcomingBookings` shows *all* slots for that date.

## 4. Key DTO Changes

- **Renamed:** `CourtManagementCardDto.bookingsTodayCount` → `bookingsCount`
- **Renamed:** `CourtManagementDetailDto.bookingsTodayCount` → `bookingsCount`
- **New Type:** `PagedResult<T>` (items, page, pageSize, totalItems, totalPages, hasNext, hasPrev)

---

> [!IMPORTANT]
> Update your API services to use the new route paths. The frontend should ideally poll `/stats` frequently and `/courts` only when necessary (filter change, page change, or manual refresh).
