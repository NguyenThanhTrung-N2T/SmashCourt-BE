# Courts API — Quick reference

Base path: `/api/courts`

Related code: [Controllers/CourtController.cs](Controllers/CourtController.cs), [Services/CourtService.cs](Services/CourtService.cs)

---

## Endpoints

- GET `/api/courts`
  - Query: `branchId` (guid, optional for internal users), `typeId` (guid)
  - Auth: public (requires `branchId`) or authenticated (auto-resolve branch)
  - Response: `ApiResponse<List<CourtDto>>`

- GET `/api/courts/{id}`
  - Query: `branchId` (guid, optional)
  - Response: `ApiResponse<CourtDto>`

- GET `/api/courts/management-dashboard/stats`
  - Query: `branchId`, `date` (yyyy-MM-dd)
  - Response: `ApiResponse<CourtManagementStatsDto>`

- GET `/api/courts/management-dashboard/courts`
  - Query: `branchId`, `date`, `search`, `typeId`, `page`, `pageSize`
  - Response: `ApiResponse<PagedResult<CourtManagementCardDto>>`

- GET `/api/courts/management-timeline`
  - Query: `branchId`, `date` (required), `typeId`
  - Response: `ApiResponse<CourtManagementTimelineDto>`

- GET `/api/courts/{id}/management-details`
  - Query: `date` (optional, defaults to today)
  - Auth: Owner/Manager
  - Response: `ApiResponse<CourtManagementDetailDto>`

- POST `/api/courts`
  - Query: `branchId` (guid, optional for Owner)
  - Body: `CreateCourtDto` (name, courtTypeId, description?, avatarUrl?)
  - Auth: Owner/Manager
  - Response: `201 Created` with `ApiResponse<CourtDto>`

- PUT `/api/courts/{id}`
  - Body: `UpdateCourtDto` (same shape as create)
  - Auth: Owner/Manager
  - Response: `ApiResponse<CourtDto>`

- POST `/api/courts/{id}/suspend`, POST `/api/courts/{id}/activate`, DELETE `/api/courts/{id}`
  - Auth: Owner/Manager
  - Response: `ApiResponse<object>` (null data on success)

---

## Key DTOs (shapes)

- CourtDto

```json
{
  "id": "guid",
  "branchId": "guid",
  "courtTypeId": "guid",
  "courtTypeName": "string",
  "name": "string",
  "description": null | "string",
  "avatarUrl": null | "string",
  "status": 0, // CourtStatus (number)
  "createdAt": "2026-05-30T...Z",
  "updatedAt": "2026-05-30T...Z"
}
```

- CourtManagementCardDto (one item in `Courts` in dashboard)

```json
{
  "id": "guid",
  "name": "Sân Thi Đấu 1",
  "typeName": "Thi đấu",
  "operationalStatus": 0, // CourtOperationalStatus
  "bookingsCount": 0,
  "basePrice": 110000,
  "scheduleTimeline": [
    { "startTime": "06:00", "endTime": "10:00", "status": 0 },
    { "startTime": "10:00", "endTime": "12:00", "status": 1 }
  ]
}
```

- CourtTimelineSlotDto

```json
{
  "startTime": "HH:mm",
  "endTime": "HH:mm",
  "status": 0 // CourtTimelineSlotStatus
}
```

- CourtManagementDetailDto (abridged)

```json
{
  "id": "guid",
  "name": "string",
  "branchName": "string",
  "operationalStatus": 0, // CourtOperationalStatus
  "typeName": "string",
  "prices": { "normalPrice": 100000, "peakPrice": 120000 },
  "currentPlayer": { "name": "string", "startTime": "HH:mm", "endTime": "HH:mm" } | null,
  "bookingsCount": 2,
  "upcomingBookings": [ { "bookingId": "guid", "timeRange": "HH:mm - HH:mm", "playerName": "string", "status": "CONFIRMED", "statusShort": "Đã XN" } ]
}
```

- PagedResult&lt;T&gt;

```json
{
  "items": [],
  "page": 1,
  "pageSize": 20,
  "totalItems": 100,
  "totalPages": 5,
  "hasNext": true,
  "hasPrev": false
}
```

- CourtManagementTimelineDto

```json
{
  "date": "yyyy-MM-dd",
  "operatingHours": { "open": "HH:mm", "close": "HH:mm" },
  "courts": [
    {
      "id": "guid",
      "name": "string",
      "typeName": "string",
      "operationalStatus": 0,
      "slots": [
        {
          "startTime": "HH:mm",
          "endTime": "HH:mm",
          "status": 0,
          "bookingId": "guid?",
          "playerName": "string?",
          "bookingStatus": "string?"
        }
      ]
    }
  ]
}
```

Notes:
- Responses use the `ApiResponse<T>` wrapper: `{ success, code, message, data }` — see [Common/ApiResponse.cs](Common/ApiResponse.cs).
- Enum values are serialized as numbers by default in the current project (see examples above).
- Timeline merging: the service coalesces contiguous 30-minute slots with the same status into single ranges to reduce payload (see [Services/CourtService.cs](Services/CourtService.cs)).

---

## Enums (values)

The following enums are used by the courts endpoints. Numeric values are the enum integer values returned in JSON.

- `CourtOperationalStatus` (derived card-level status)

```
READY = 0
BOOKED = 1
PLAYING = 2
SUSPENDED = 3
```

- `CourtTimelineSlotStatus` (per timeline range)

```
AVAILABLE = 0
BOOKED = 1
PLAYING = 2
```

- `CourtStatus` (domain state for a court)

```
AVAILABLE = 0
LOCKED = 1
IN_USE = 3
SUSPENDED = 4
INACTIVE = 5
```

- `BookingStatus` (booking lifecycle)

```
PENDING = 0
CONFIRMED = 1
PAID_ONLINE = 2
IN_PROGRESS = 3
PENDING_PAYMENT = 4
COMPLETED = 5
CANCELLED = 6
CANCELLED_PENDING_REFUND = 7
CANCELLED_REFUNDED = 8
NO_SHOW = 9
```

- `CourtTypeStatus`

```
ACTIVE = 0
DELETED = 1
```

- `BranchStatus`

```
ACTIVE = 0
SUSPENDED = 1
INACTIVE = 2
```

- `DayType` (used when selecting time slots/prices)

```
WEEKDAY = 0
WEEKEND = 1
```

---

## Implementation notes

- Timeline granularity comes from `TimeSlot` data (commonly 30 minutes). The service merges adjacent slots with identical `CourtTimelineSlotStatus` into wider ranges.
- The dashboard derives `CourtOperationalStatus` by checking (in order): `Court.Status == SUSPENDED` → `SUSPENDED`; overlapping `IN_PROGRESS` bookings → `PLAYING`; overlapping or upcoming bookings in active states (`CONFIRMED`, `PAID_ONLINE`, `PENDING_PAYMENT`, `PENDING`) → `BOOKED`; otherwise `READY`.

---

File: [docs/courts-endpoints.md](docs/courts-endpoints.md)
