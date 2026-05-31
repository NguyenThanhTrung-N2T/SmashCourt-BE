/**
 * Report Types
 * 
 * TypeScript definitions for report-related DTOs and queries.
 */

export interface ReportFilterDto {
    fromDate?: string; // format YYYY-MM-DD
    toDate?: string;   // format YYYY-MM-DD
    branchId?: string; // Guid
    groupBy?: 'day' | 'week' | 'month' | 'branch' | 'courtType' | 'paymentMethod' | 'hour' | 'dayOfWeek';
}

export interface DashboardSummaryDto {
    totalRevenue: number;
    totalBookings: number;
    completedBookings: number;
    cancelledBookings: number;
    noShowBookings: number;
    newCustomers: number;
    occupancyRate: number;
    onlinePaymentRevenue: number;
    cashPaymentRevenue: number;
}

export interface TopBranchDto {
    branchId: string;
    branchName: string;
    revenue: number;
    bookingCount: number;
}

export interface TopCustomerDto {
    customerId: string;
    fullName: string;
    totalRevenue: number;
    bookingCount: number;
    loyaltyTier: string;
}

export interface RevenueTrendDto {
    period: string;
    revenue: number;
    bookingCount: number;
}

export interface BookingTrendDto {
    period: string;
    totalCount: number;
    completedCount: number;
}

export interface OwnerDashboardDto {
    summary: DashboardSummaryDto;
    topBranches: TopBranchDto[];
    topCustomers: TopCustomerDto[];
    revenueTrend: RevenueTrendDto[];
    bookingTrend: BookingTrendDto[];
}

export interface ManagerDashboardDto {
    summary: DashboardSummaryDto;
    topCustomers: TopCustomerDto[];
    revenueTrend: RevenueTrendDto[];
    bookingTrend: BookingTrendDto[];
}

export interface RevenueItemDto {
    period: string;
    revenue: number;
    bookingCount: number;
}

export interface RevenueReportDto {
    totalRevenue: number;
    courtRevenue: number;
    serviceRevenue: number;
    discountAmount: number;
    averageBookingValue: number;
    items: RevenueItemDto[];
}

export interface BookingItemDto {
    period: string;
    bookingCount: number;
    completedCount: number;
    cancelledCount: number;
}

export interface BookingReportDto {
    totalBookings: number;
    completed: number;
    cancelled: number;
    noShow: number;
    pendingPayment: number;
    onlineBookings: number;
    walkInBookings: number;
    cancellationRate: number;
    noShowRate: number;
    items: BookingItemDto[];
}

export interface PeakHourDto {
    hour: number;
    bookingCount: number;
    occupancyRate: number;
}

export interface CourtUtilizationItemDto {
    courtId: string;
    courtName: string;
    period: string;
    bookedHours: number;
    availableHours: number;
    occupancyRate: number;
}

export interface CourtUtilizationReportDto {
    overallOccupancyRate: number;
    totalAvailableHours: number;
    totalBookedHours: number;
    peakHours: PeakHourDto[];
    offPeakHours: PeakHourDto[];
    topCourts: CourtUtilizationItemDto[];
    items: CourtUtilizationItemDto[];
}

export interface LoyaltyTierDistributionDto {
    tierName: string;
    customerCount: number;
    percentage: number;
}

export interface CustomerAcquisitionTrendDto {
    period: string;
    newCustomers: number;
}

export interface CustomerStatisticsReportDto {
    totalCustomers: number;
    newCustomers: number;
    repeatCustomers: number;
    repeatCustomerRate: number;
    averageBookingsPerCustomer: number;
    averageRevenuePerCustomer: number;
    loyaltyTierDistribution: LoyaltyTierDistributionDto[];
    acquisitionTrend: CustomerAcquisitionTrendDto[];
}

export interface TopSpenderDto {
    customerId: string;
    fullName: string;
    email: string;
    phone: string;
    totalRevenue: number;
    bookingCount: number;
    loyaltyTier: string;
}

export interface TopSpendersReportDto {
    totalCount: number;
    page: number;
    pageSize: number;
    items: TopSpenderDto[];
}

export interface ServiceItemDto {
    serviceId: string;
    serviceName: string;
    revenue: number;
    bookingCount: number;
    averageRevenue: number;
}

export interface ServiceTrendDto {
    period: string;
    serviceRevenue: number;
    bookingCount: number;
}

export interface ServicePerformanceReportDto {
    totalServiceRevenue: number;
    totalBookingsWithServices: number;
    serviceAttachmentRate: number;
    averageServiceRevenuePerBooking: number;
    topServices: ServiceItemDto[];
    serviceTrend: ServiceTrendDto[];
}

export interface PromotionItemDto {
    promotionId: string;
    promotionName: string;
    promotionCode: string;
    usageCount: number;
    totalDiscount: number;
    revenueAfterDiscount: number;
    averageDiscount: number;
}

export interface PromotionTrendDto {
    period: string;
    usageCount: number;
    totalDiscount: number;
}

export interface PromotionEffectivenessReportDto {
    totalDiscountAmount: number;
    totalPromotionUsage: number;
    averageDiscountPerUsage: number;
    promotionConversionRate: number;
    topPromotions: PromotionItemDto[];
    promotionTrend: PromotionTrendDto[];
}

/**
 * Reports API
 * 
 * API endpoints for report and dashboard data.
 */

import { authProtectedFetch } from "./core";
import {
    ReportFilterDto,
    OwnerDashboardDto,
    ManagerDashboardDto,
    RevenueReportDto,
    BookingReportDto,
    CourtUtilizationReportDto,
    CustomerStatisticsReportDto,
    TopSpendersReportDto,
    ServicePerformanceReportDto,
    PromotionEffectivenessReportDto
} from "../features/report/shared/types/report.types";

/**
 * Append common report filters to URLSearchParams.
 */
function appendReportFilterParams(params: URLSearchParams, filter: ReportFilterDto): void {
    if (filter.fromDate) params.append("fromDate", filter.fromDate);
    if (filter.toDate) params.append("toDate", filter.toDate);
    if (filter.branchId) params.append("branchId", filter.branchId);
    if (filter.groupBy) params.append("groupBy", filter.groupBy);
}

/**
 * Get owner dashboard data.
 */
export async function fetchOwnerDashboard(
    filter: ReportFilterDto = {}
): Promise<OwnerDashboardDto> {
    const params = new URLSearchParams();
    appendReportFilterParams(params, filter);

    const response = await authProtectedFetch<OwnerDashboardDto>(
        `/api/reports/dashboard/owner?${params}`,
        { method: "GET" }
    );
    if (!response.data) throw new Error("Failed to fetch owner dashboard data");
    return response.data;
}

/**
 * Get manager dashboard data.
 */
export async function fetchManagerDashboard(
    filter: ReportFilterDto = {}
): Promise<ManagerDashboardDto> {
    const params = new URLSearchParams();
    appendReportFilterParams(params, filter);

    const response = await authProtectedFetch<ManagerDashboardDto>(
        `/api/reports/dashboard/manager?${params}`,
        { method: "GET" }
    );
    if (!response.data) throw new Error("Failed to fetch manager dashboard data");
    return response.data;
}

/**
 * Get revenue report data.
 */
export async function fetchRevenueReport(
    filter: ReportFilterDto = {}
): Promise<RevenueReportDto> {
    const params = new URLSearchParams();
    appendReportFilterParams(params, filter);

    const response = await authProtectedFetch<RevenueReportDto>(
        `/api/reports/revenue?${params}`,
        { method: "GET" }
    );
    if (!response.data) throw new Error("Failed to fetch revenue report data");
    return response.data;
}

/**
 * Get booking report data.
 */
export async function fetchBookingReport(
    filter: ReportFilterDto = {}
): Promise<BookingReportDto> {
    const params = new URLSearchParams();
    appendReportFilterParams(params, filter);

    const response = await authProtectedFetch<BookingReportDto>(
        `/api/reports/bookings?${params}`,
        { method: "GET" }
    );
    if (!response.data) throw new Error("Failed to fetch booking report data");
    return response.data;
}

/**
 * Get court utilization report data.
 */
export async function fetchCourtUtilizationReport(
    filter: ReportFilterDto = {}
): Promise<CourtUtilizationReportDto> {
    const params = new URLSearchParams();
    appendReportFilterParams(params, filter);

    const response = await authProtectedFetch<CourtUtilizationReportDto>(
        `/api/reports/courts/utilization?${params}`,
        { method: "GET" }
    );
    if (!response.data) throw new Error("Failed to fetch court utilization report data");
    return response.data;
}

/**
 * Get customer statistics report data.
 */
export async function fetchCustomerStatisticsReport(
    filter: ReportFilterDto = {}
): Promise<CustomerStatisticsReportDto> {
    const params = new URLSearchParams();
    appendReportFilterParams(params, filter);

    const response = await authProtectedFetch<CustomerStatisticsReportDto>(
        `/api/reports/customers?${params}`,
        { method: "GET" }
    );
    if (!response.data) throw new Error("Failed to fetch customer statistics report data");
    return response.data;
}

/**
 * Get top spenders report data (paginated).
 */
export async function fetchTopSpendersReport(
    filter: ReportFilterDto & { page?: number; pageSize?: number } = {}
): Promise<TopSpendersReportDto> {
    const params = new URLSearchParams({
        page: (filter.page || 1).toString(),
        pageSize: (filter.pageSize || 20).toString(),
    });
    appendReportFilterParams(params, filter);

    const response = await authProtectedFetch<TopSpendersReportDto>(
        `/api/reports/customers/top-spenders?${params}`,
        { method: "GET" }
    );
    if (!response.data) throw new Error("Failed to fetch top spenders report data");
    return response.data;
}

/**
 * Get service performance report data.
 */
export async function fetchServicePerformanceReport(
    filter: ReportFilterDto = {}
): Promise<ServicePerformanceReportDto> {
    const params = new URLSearchParams();
    appendReportFilterParams(params, filter);

    const response = await authProtectedFetch<ServicePerformanceReportDto>(
        `/api/reports/services?${params}`,
        { method: "GET" }
    );
    if (!response.data) throw new Error("Failed to fetch service performance report data");
    return response.data;
}

/**
 * Get promotion effectiveness report data.
 */
export async function fetchPromotionEffectivenessReport(
    filter: ReportFilterDto = {}
): Promise<PromotionEffectivenessReportDto> {
    const params = new URLSearchParams();
    appendReportFilterParams(params, filter);

    const response = await authProtectedFetch<PromotionEffectivenessReportDto>(
        `/api/reports/promotions?${params}`,
        { method: "GET" }
    );
    if (!response.data) throw new Error("Failed to fetch promotion effectiveness report data");
    return response.data;
}
