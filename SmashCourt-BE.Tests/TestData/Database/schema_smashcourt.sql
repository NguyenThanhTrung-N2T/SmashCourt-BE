--
-- PostgreSQL database dump
--

\restrict lu8vQKehY1DdNFTcqaIqN1F5Q8BMfnOcv4o1K7RbHL9c7bP5aEfCQfBchIMTRCa

-- Dumped from database version 17.11 (59c914d)
-- Dumped by pg_dump version 17.11

SET statement_timeout = 0;
SET lock_timeout = 0;
SET idle_in_transaction_session_timeout = 0;
SET transaction_timeout = 0;
SET client_encoding = 'UTF8';
SET standard_conforming_strings = on;
SELECT pg_catalog.set_config('search_path', '', false);
SET check_function_bodies = false;
SET xmloption = content;
SET client_min_messages = warning;
SET row_security = off;

--
-- Name: hangfire; Type: SCHEMA; Schema: -; Owner: neondb_owner
--

CREATE SCHEMA hangfire;


ALTER SCHEMA hangfire OWNER TO neondb_owner;

--
-- Name: btree_gist; Type: EXTENSION; Schema: -; Owner: -
--

CREATE EXTENSION IF NOT EXISTS btree_gist WITH SCHEMA public;


--
-- Name: EXTENSION btree_gist; Type: COMMENT; Schema: -; Owner: 
--

COMMENT ON EXTENSION btree_gist IS 'support for indexing common datatypes in GiST';


--
-- Name: pgcrypto; Type: EXTENSION; Schema: -; Owner: -
--

CREATE EXTENSION IF NOT EXISTS pgcrypto WITH SCHEMA public;


--
-- Name: EXTENSION pgcrypto; Type: COMMENT; Schema: -; Owner: 
--

COMMENT ON EXTENSION pgcrypto IS 'cryptographic functions';


--
-- Name: approval_result; Type: TYPE; Schema: public; Owner: neondb_owner
--

CREATE TYPE public.approval_result AS ENUM (
    'APPROVED',
    'REJECTED'
);


ALTER TYPE public.approval_result OWNER TO neondb_owner;

--
-- Name: booking_cancel_reason; Type: TYPE; Schema: public; Owner: neondb_owner
--

CREATE TYPE public.booking_cancel_reason AS ENUM (
    'PAYMENT_EXPIRED',
    'APPROVAL_EXPIRED',
    'APPROVAL_REJECTED',
    'POLICY_REJECTED',
    'CUSTOMER_CANCELLED',
    'STAFF_CANCELLED',
    'SYSTEM_CLEANUP'
);


ALTER TYPE public.booking_cancel_reason OWNER TO neondb_owner;

--
-- Name: booking_source; Type: TYPE; Schema: public; Owner: neondb_owner
--

CREATE TYPE public.booking_source AS ENUM (
    'ONLINE',
    'WALK_IN'
);


ALTER TYPE public.booking_source OWNER TO neondb_owner;

--
-- Name: booking_status; Type: TYPE; Schema: public; Owner: neondb_owner
--

CREATE TYPE public.booking_status AS ENUM (
    'PENDING',
    'CONFIRMED',
    'PAID_ONLINE',
    'IN_PROGRESS',
    'PENDING_PAYMENT',
    'COMPLETED',
    'CANCELLED',
    'CANCELLED_PENDING_REFUND',
    'CANCELLED_REFUNDED',
    'NO_SHOW',
    'PENDING_APPROVAL'
);


ALTER TYPE public.booking_status OWNER TO neondb_owner;

--
-- Name: branch_service_status; Type: TYPE; Schema: public; Owner: neondb_owner
--

CREATE TYPE public.branch_service_status AS ENUM (
    'ENABLED',
    'DISABLED'
);


ALTER TYPE public.branch_service_status OWNER TO neondb_owner;

--
-- Name: branch_status; Type: TYPE; Schema: public; Owner: neondb_owner
--

CREATE TYPE public.branch_status AS ENUM (
    'ACTIVE',
    'SUSPENDED',
    'INACTIVE'
);


ALTER TYPE public.branch_status OWNER TO neondb_owner;

--
-- Name: cancel_source_enum; Type: TYPE; Schema: public; Owner: neondb_owner
--

CREATE TYPE public.cancel_source_enum AS ENUM (
    'LINK',
    'STAFF',
    'SYSTEM',
    'CUSTOMER'
);


ALTER TYPE public.cancel_source_enum OWNER TO neondb_owner;

--
-- Name: court_status; Type: TYPE; Schema: public; Owner: neondb_owner
--

CREATE TYPE public.court_status AS ENUM (
    'AVAILABLE',
    'LOCKED',
    'IN_USE',
    'SUSPENDED',
    'INACTIVE'
);


ALTER TYPE public.court_status OWNER TO neondb_owner;

--
-- Name: court_status_old; Type: TYPE; Schema: public; Owner: neondb_owner
--

CREATE TYPE public.court_status_old AS ENUM (
    'AVAILABLE',
    'LOCKED',
    'BOOKED',
    'IN_USE',
    'SUSPENDED',
    'INACTIVE'
);


ALTER TYPE public.court_status_old OWNER TO neondb_owner;

--
-- Name: court_type_status; Type: TYPE; Schema: public; Owner: neondb_owner
--

CREATE TYPE public.court_type_status AS ENUM (
    'ACTIVE',
    'DELETED'
);


ALTER TYPE public.court_type_status OWNER TO neondb_owner;

--
-- Name: day_type; Type: TYPE; Schema: public; Owner: neondb_owner
--

CREATE TYPE public.day_type AS ENUM (
    'WEEKDAY',
    'WEEKEND'
);


ALTER TYPE public.day_type OWNER TO neondb_owner;

--
-- Name: deposit_source; Type: TYPE; Schema: public; Owner: neondb_owner
--

CREATE TYPE public.deposit_source AS ENUM (
    'TADSS',
    'MANAGER',
    'FULL_PAYMENT_FALLBACK'
);


ALTER TYPE public.deposit_source OWNER TO neondb_owner;

--
-- Name: discount_type_enum; Type: TYPE; Schema: public; Owner: neondb_owner
--

CREATE TYPE public.discount_type_enum AS ENUM (
    'PERCENT',
    'FIXED'
);


ALTER TYPE public.discount_type_enum OWNER TO neondb_owner;

--
-- Name: invoice_payment_status; Type: TYPE; Schema: public; Owner: neondb_owner
--

CREATE TYPE public.invoice_payment_status AS ENUM (
    'UNPAID',
    'PARTIALLY_PAID',
    'PAID',
    'REFUNDED',
    'EXPIRED'
);


ALTER TYPE public.invoice_payment_status OWNER TO neondb_owner;

--
-- Name: ipn_provider; Type: TYPE; Schema: public; Owner: neondb_owner
--

CREATE TYPE public.ipn_provider AS ENUM (
    'VNPAY',
    'MOMO'
);


ALTER TYPE public.ipn_provider OWNER TO neondb_owner;

--
-- Name: loyalty_transaction_type; Type: TYPE; Schema: public; Owner: neondb_owner
--

CREATE TYPE public.loyalty_transaction_type AS ENUM (
    'EARN',
    'ADJUST',
    'DEDUCT'
);


ALTER TYPE public.loyalty_transaction_type OWNER TO neondb_owner;

--
-- Name: otp_type; Type: TYPE; Schema: public; Owner: neondb_owner
--

CREATE TYPE public.otp_type AS ENUM (
    'EMAIL_VERIFY',
    'FORGOT_PASSWORD',
    'TWO_FA',
    'ENABLE_2FA',
    'DISABLE_2FA'
);


ALTER TYPE public.otp_type OWNER TO neondb_owner;

--
-- Name: payment_tx_method; Type: TYPE; Schema: public; Owner: neondb_owner
--

CREATE TYPE public.payment_tx_method AS ENUM (
    'CASH',
    'VNPAY',
    'MOMO'
);


ALTER TYPE public.payment_tx_method OWNER TO neondb_owner;

--
-- Name: payment_tx_status; Type: TYPE; Schema: public; Owner: neondb_owner
--

CREATE TYPE public.payment_tx_status AS ENUM (
    'PENDING',
    'SUCCESS',
    'FAILED',
    'REFUNDED'
);


ALTER TYPE public.payment_tx_status OWNER TO neondb_owner;

--
-- Name: promotion_status; Type: TYPE; Schema: public; Owner: neondb_owner
--

CREATE TYPE public.promotion_status AS ENUM (
    'ACTIVE',
    'INACTIVE',
    'DELETED'
);


ALTER TYPE public.promotion_status OWNER TO neondb_owner;

--
-- Name: refund_status; Type: TYPE; Schema: public; Owner: neondb_owner
--

CREATE TYPE public.refund_status AS ENUM (
    'PENDING',
    'COMPLETED',
    'FAILED'
);


ALTER TYPE public.refund_status OWNER TO neondb_owner;

--
-- Name: service_status; Type: TYPE; Schema: public; Owner: neondb_owner
--

CREATE TYPE public.service_status AS ENUM (
    'ACTIVE',
    'DELETED'
);


ALTER TYPE public.service_status OWNER TO neondb_owner;

--
-- Name: user_branch_role; Type: TYPE; Schema: public; Owner: neondb_owner
--

CREATE TYPE public.user_branch_role AS ENUM (
    'MANAGER',
    'STAFF'
);


ALTER TYPE public.user_branch_role OWNER TO neondb_owner;

--
-- Name: user_role; Type: TYPE; Schema: public; Owner: neondb_owner
--

CREATE TYPE public.user_role AS ENUM (
    'CUSTOMER',
    'STAFF',
    'BRANCH_MANAGER',
    'OWNER'
);


ALTER TYPE public.user_role OWNER TO neondb_owner;

--
-- Name: user_status; Type: TYPE; Schema: public; Owner: neondb_owner
--

CREATE TYPE public.user_status AS ENUM (
    'ACTIVE',
    'LOCKED',
    'INACTIVE'
);


ALTER TYPE public.user_status OWNER TO neondb_owner;

SET default_tablespace = '';

SET default_table_access_method = heap;

--
-- Name: aggregatedcounter; Type: TABLE; Schema: hangfire; Owner: neondb_owner
--

CREATE TABLE hangfire.aggregatedcounter (
    id bigint NOT NULL,
    key text NOT NULL,
    value bigint NOT NULL,
    expireat timestamp with time zone
);


ALTER TABLE hangfire.aggregatedcounter OWNER TO neondb_owner;

--
-- Name: aggregatedcounter_id_seq; Type: SEQUENCE; Schema: hangfire; Owner: neondb_owner
--

CREATE SEQUENCE hangfire.aggregatedcounter_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


ALTER SEQUENCE hangfire.aggregatedcounter_id_seq OWNER TO neondb_owner;

--
-- Name: aggregatedcounter_id_seq; Type: SEQUENCE OWNED BY; Schema: hangfire; Owner: neondb_owner
--

ALTER SEQUENCE hangfire.aggregatedcounter_id_seq OWNED BY hangfire.aggregatedcounter.id;


--
-- Name: counter; Type: TABLE; Schema: hangfire; Owner: neondb_owner
--

CREATE TABLE hangfire.counter (
    id bigint NOT NULL,
    key text NOT NULL,
    value bigint NOT NULL,
    expireat timestamp with time zone
);


ALTER TABLE hangfire.counter OWNER TO neondb_owner;

--
-- Name: counter_id_seq; Type: SEQUENCE; Schema: hangfire; Owner: neondb_owner
--

CREATE SEQUENCE hangfire.counter_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


ALTER SEQUENCE hangfire.counter_id_seq OWNER TO neondb_owner;

--
-- Name: counter_id_seq; Type: SEQUENCE OWNED BY; Schema: hangfire; Owner: neondb_owner
--

ALTER SEQUENCE hangfire.counter_id_seq OWNED BY hangfire.counter.id;


--
-- Name: hash; Type: TABLE; Schema: hangfire; Owner: neondb_owner
--

CREATE TABLE hangfire.hash (
    id bigint NOT NULL,
    key text NOT NULL,
    field text NOT NULL,
    value text,
    expireat timestamp with time zone,
    updatecount integer DEFAULT 0 NOT NULL
);


ALTER TABLE hangfire.hash OWNER TO neondb_owner;

--
-- Name: hash_id_seq; Type: SEQUENCE; Schema: hangfire; Owner: neondb_owner
--

CREATE SEQUENCE hangfire.hash_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


ALTER SEQUENCE hangfire.hash_id_seq OWNER TO neondb_owner;

--
-- Name: hash_id_seq; Type: SEQUENCE OWNED BY; Schema: hangfire; Owner: neondb_owner
--

ALTER SEQUENCE hangfire.hash_id_seq OWNED BY hangfire.hash.id;


--
-- Name: job; Type: TABLE; Schema: hangfire; Owner: neondb_owner
--

CREATE TABLE hangfire.job (
    id bigint NOT NULL,
    stateid bigint,
    statename text,
    invocationdata jsonb NOT NULL,
    arguments jsonb NOT NULL,
    createdat timestamp with time zone NOT NULL,
    expireat timestamp with time zone,
    updatecount integer DEFAULT 0 NOT NULL
);


ALTER TABLE hangfire.job OWNER TO neondb_owner;

--
-- Name: job_id_seq; Type: SEQUENCE; Schema: hangfire; Owner: neondb_owner
--

CREATE SEQUENCE hangfire.job_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


ALTER SEQUENCE hangfire.job_id_seq OWNER TO neondb_owner;

--
-- Name: job_id_seq; Type: SEQUENCE OWNED BY; Schema: hangfire; Owner: neondb_owner
--

ALTER SEQUENCE hangfire.job_id_seq OWNED BY hangfire.job.id;


--
-- Name: jobparameter; Type: TABLE; Schema: hangfire; Owner: neondb_owner
--

CREATE TABLE hangfire.jobparameter (
    id bigint NOT NULL,
    jobid bigint NOT NULL,
    name text NOT NULL,
    value text,
    updatecount integer DEFAULT 0 NOT NULL
);


ALTER TABLE hangfire.jobparameter OWNER TO neondb_owner;

--
-- Name: jobparameter_id_seq; Type: SEQUENCE; Schema: hangfire; Owner: neondb_owner
--

CREATE SEQUENCE hangfire.jobparameter_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


ALTER SEQUENCE hangfire.jobparameter_id_seq OWNER TO neondb_owner;

--
-- Name: jobparameter_id_seq; Type: SEQUENCE OWNED BY; Schema: hangfire; Owner: neondb_owner
--

ALTER SEQUENCE hangfire.jobparameter_id_seq OWNED BY hangfire.jobparameter.id;


--
-- Name: jobqueue; Type: TABLE; Schema: hangfire; Owner: neondb_owner
--

CREATE TABLE hangfire.jobqueue (
    id bigint NOT NULL,
    jobid bigint NOT NULL,
    queue text NOT NULL,
    fetchedat timestamp with time zone,
    updatecount integer DEFAULT 0 NOT NULL
);


ALTER TABLE hangfire.jobqueue OWNER TO neondb_owner;

--
-- Name: jobqueue_id_seq; Type: SEQUENCE; Schema: hangfire; Owner: neondb_owner
--

CREATE SEQUENCE hangfire.jobqueue_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


ALTER SEQUENCE hangfire.jobqueue_id_seq OWNER TO neondb_owner;

--
-- Name: jobqueue_id_seq; Type: SEQUENCE OWNED BY; Schema: hangfire; Owner: neondb_owner
--

ALTER SEQUENCE hangfire.jobqueue_id_seq OWNED BY hangfire.jobqueue.id;


--
-- Name: list; Type: TABLE; Schema: hangfire; Owner: neondb_owner
--

CREATE TABLE hangfire.list (
    id bigint NOT NULL,
    key text NOT NULL,
    value text,
    expireat timestamp with time zone,
    updatecount integer DEFAULT 0 NOT NULL
);


ALTER TABLE hangfire.list OWNER TO neondb_owner;

--
-- Name: list_id_seq; Type: SEQUENCE; Schema: hangfire; Owner: neondb_owner
--

CREATE SEQUENCE hangfire.list_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


ALTER SEQUENCE hangfire.list_id_seq OWNER TO neondb_owner;

--
-- Name: list_id_seq; Type: SEQUENCE OWNED BY; Schema: hangfire; Owner: neondb_owner
--

ALTER SEQUENCE hangfire.list_id_seq OWNED BY hangfire.list.id;


--
-- Name: lock; Type: TABLE; Schema: hangfire; Owner: neondb_owner
--

CREATE TABLE hangfire.lock (
    resource text NOT NULL,
    updatecount integer DEFAULT 0 NOT NULL,
    acquired timestamp with time zone
);


ALTER TABLE hangfire.lock OWNER TO neondb_owner;

--
-- Name: schema; Type: TABLE; Schema: hangfire; Owner: neondb_owner
--

CREATE TABLE hangfire.schema (
    version integer NOT NULL
);


ALTER TABLE hangfire.schema OWNER TO neondb_owner;

--
-- Name: server; Type: TABLE; Schema: hangfire; Owner: neondb_owner
--

CREATE TABLE hangfire.server (
    id text NOT NULL,
    data jsonb,
    lastheartbeat timestamp with time zone NOT NULL,
    updatecount integer DEFAULT 0 NOT NULL
);


ALTER TABLE hangfire.server OWNER TO neondb_owner;

--
-- Name: set; Type: TABLE; Schema: hangfire; Owner: neondb_owner
--

CREATE TABLE hangfire.set (
    id bigint NOT NULL,
    key text NOT NULL,
    score double precision NOT NULL,
    value text NOT NULL,
    expireat timestamp with time zone,
    updatecount integer DEFAULT 0 NOT NULL
);


ALTER TABLE hangfire.set OWNER TO neondb_owner;

--
-- Name: set_id_seq; Type: SEQUENCE; Schema: hangfire; Owner: neondb_owner
--

CREATE SEQUENCE hangfire.set_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


ALTER SEQUENCE hangfire.set_id_seq OWNER TO neondb_owner;

--
-- Name: set_id_seq; Type: SEQUENCE OWNED BY; Schema: hangfire; Owner: neondb_owner
--

ALTER SEQUENCE hangfire.set_id_seq OWNED BY hangfire.set.id;


--
-- Name: state; Type: TABLE; Schema: hangfire; Owner: neondb_owner
--

CREATE TABLE hangfire.state (
    id bigint NOT NULL,
    jobid bigint NOT NULL,
    name text NOT NULL,
    reason text,
    createdat timestamp with time zone NOT NULL,
    data jsonb,
    updatecount integer DEFAULT 0 NOT NULL
);


ALTER TABLE hangfire.state OWNER TO neondb_owner;

--
-- Name: state_id_seq; Type: SEQUENCE; Schema: hangfire; Owner: neondb_owner
--

CREATE SEQUENCE hangfire.state_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


ALTER SEQUENCE hangfire.state_id_seq OWNER TO neondb_owner;

--
-- Name: state_id_seq; Type: SEQUENCE OWNED BY; Schema: hangfire; Owner: neondb_owner
--

ALTER SEQUENCE hangfire.state_id_seq OWNED BY hangfire.state.id;


--
-- Name: booking_code_seq; Type: SEQUENCE; Schema: public; Owner: neondb_owner
--

CREATE SEQUENCE public.booking_code_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


ALTER SEQUENCE public.booking_code_seq OWNER TO neondb_owner;

--
-- Name: booking_courts; Type: TABLE; Schema: public; Owner: neondb_owner
--

CREATE TABLE public.booking_courts (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    booking_id uuid NOT NULL,
    court_id uuid NOT NULL,
    date date NOT NULL,
    start_time time without time zone NOT NULL,
    end_time time without time zone NOT NULL,
    is_active boolean DEFAULT true NOT NULL,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    actual_end_play_time time without time zone,
    CONSTRAINT chk_booking_courts_valid_time CHECK ((end_time > start_time))
);


ALTER TABLE public.booking_courts OWNER TO neondb_owner;

--
-- Name: booking_price_items; Type: TABLE; Schema: public; Owner: neondb_owner
--

CREATE TABLE public.booking_price_items (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    booking_court_id uuid NOT NULL,
    time_slot_id uuid NOT NULL,
    unit_price numeric(12,2) NOT NULL,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    CONSTRAINT chk_booking_price_items_positive CHECK ((unit_price > (0)::numeric))
);


ALTER TABLE public.booking_price_items OWNER TO neondb_owner;

--
-- Name: booking_promotions; Type: TABLE; Schema: public; Owner: neondb_owner
--

CREATE TABLE public.booking_promotions (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    booking_id uuid NOT NULL,
    promotion_id uuid NOT NULL,
    promotion_name_snapshot character varying(255) NOT NULL,
    discount_value_snapshot numeric(12,2) NOT NULL,
    discount_amount numeric(12,2) NOT NULL,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    promotion_code_snapshot character varying(50),
    discount_type_snapshot public.discount_type_enum DEFAULT 'PERCENT'::public.discount_type_enum NOT NULL,
    CONSTRAINT chk_booking_promotions_amount CHECK ((discount_amount >= (0)::numeric)),
    CONSTRAINT chk_booking_promotions_discount_value CHECK ((((discount_type_snapshot = 'PERCENT'::public.discount_type_enum) AND (discount_value_snapshot > (0)::numeric) AND (discount_value_snapshot <= (100)::numeric)) OR ((discount_type_snapshot = 'FIXED'::public.discount_type_enum) AND (discount_value_snapshot > (0)::numeric) AND (discount_value_snapshot <= 99999999.99))))
);


ALTER TABLE public.booking_promotions OWNER TO neondb_owner;

--
-- Name: booking_services; Type: TABLE; Schema: public; Owner: neondb_owner
--

CREATE TABLE public.booking_services (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    booking_id uuid NOT NULL,
    service_id uuid NOT NULL,
    service_name character varying(255) NOT NULL,
    unit character varying(50) NOT NULL,
    unit_price numeric(12,2) NOT NULL,
    quantity integer DEFAULT 1 NOT NULL,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    CONSTRAINT chk_booking_services_price CHECK ((unit_price > (0)::numeric)),
    CONSTRAINT chk_booking_services_quantity CHECK ((quantity > 0))
);


ALTER TABLE public.booking_services OWNER TO neondb_owner;

--
-- Name: bookings; Type: TABLE; Schema: public; Owner: neondb_owner
--

CREATE TABLE public.bookings (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    branch_id uuid NOT NULL,
    customer_id uuid,
    guest_name character varying(255),
    guest_phone character varying(20),
    guest_email character varying(255),
    booking_date date NOT NULL,
    status public.booking_status DEFAULT 'PENDING'::public.booking_status NOT NULL,
    source public.booking_source NOT NULL,
    note text,
    created_by uuid,
    cancelled_by uuid,
    cancelled_at timestamp with time zone,
    cancel_source public.cancel_source_enum,
    cancel_token_hash character varying(255),
    cancel_token_expires_at timestamp with time zone,
    cancel_token_used_at timestamp with time zone,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    updated_at timestamp with time zone DEFAULT now() NOT NULL,
    checked_in_at timestamp with time zone,
    booking_code character varying(20) NOT NULL,
    tadss_decision_id uuid,
    approval_expires_at timestamp with time zone,
    approval_reason_code character varying(64),
    approval_reasons text[],
    approval_result public.approval_result,
    approval_resolved_by uuid,
    approval_resolved_at timestamp with time zone,
    approval_resolution_note character varying(500),
    cancel_reason public.booking_cancel_reason,
    tadss_outcome_reported_at timestamp with time zone,
    CONSTRAINT chk_bookings_approval_resolution CHECK (((approval_result IS NULL) = (approval_resolved_at IS NULL))),
    CONSTRAINT chk_bookings_cancel_reason CHECK (((cancel_reason IS NULL) OR (status = ANY (ARRAY['CANCELLED'::public.booking_status, 'CANCELLED_PENDING_REFUND'::public.booking_status, 'CANCELLED_REFUNDED'::public.booking_status])))),
    CONSTRAINT chk_bookings_cancelled_at CHECK ((((status = ANY (ARRAY['CANCELLED'::public.booking_status, 'CANCELLED_PENDING_REFUND'::public.booking_status, 'CANCELLED_REFUNDED'::public.booking_status])) AND (cancelled_at IS NOT NULL)) OR (status <> ALL (ARRAY['CANCELLED'::public.booking_status, 'CANCELLED_PENDING_REFUND'::public.booking_status, 'CANCELLED_REFUNDED'::public.booking_status])))),
    CONSTRAINT chk_bookings_guest_info CHECK (((customer_id IS NOT NULL) OR (NULLIF(TRIM(BOTH FROM guest_name), ''::text) IS NOT NULL) OR (NULLIF(TRIM(BOTH FROM guest_phone), ''::text) IS NOT NULL))),
    CONSTRAINT chk_bookings_pending_approval CHECK (((status <> 'PENDING_APPROVAL'::public.booking_status) OR ((tadss_decision_id IS NOT NULL) AND (approval_expires_at IS NOT NULL))))
);


ALTER TABLE public.bookings OWNER TO neondb_owner;

--
-- Name: branch_court_types; Type: TABLE; Schema: public; Owner: neondb_owner
--

CREATE TABLE public.branch_court_types (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    branch_id uuid NOT NULL,
    court_type_id uuid NOT NULL,
    is_active boolean DEFAULT true NOT NULL,
    created_at timestamp with time zone DEFAULT now() NOT NULL
);


ALTER TABLE public.branch_court_types OWNER TO neondb_owner;

--
-- Name: branch_price_overrides; Type: TABLE; Schema: public; Owner: neondb_owner
--

CREATE TABLE public.branch_price_overrides (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    branch_id uuid NOT NULL,
    court_type_id uuid NOT NULL,
    time_slot_id uuid NOT NULL,
    price numeric(12,2) NOT NULL,
    effective_from date NOT NULL,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    CONSTRAINT chk_branch_price_overrides_positive CHECK ((price > (0)::numeric))
);


ALTER TABLE public.branch_price_overrides OWNER TO neondb_owner;

--
-- Name: branch_services; Type: TABLE; Schema: public; Owner: neondb_owner
--

CREATE TABLE public.branch_services (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    branch_id uuid NOT NULL,
    service_id uuid NOT NULL,
    price numeric(12,2) NOT NULL,
    status public.branch_service_status DEFAULT 'ENABLED'::public.branch_service_status NOT NULL,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    updated_at timestamp with time zone DEFAULT now() NOT NULL,
    CONSTRAINT chk_branch_services_price_positive CHECK ((price > (0)::numeric))
);


ALTER TABLE public.branch_services OWNER TO neondb_owner;

--
-- Name: branches; Type: TABLE; Schema: public; Owner: neondb_owner
--

CREATE TABLE public.branches (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    name character varying(255) NOT NULL,
    address text NOT NULL,
    latitude numeric(10,7),
    longitude numeric(10,7),
    phone character varying(20),
    avatar_url text,
    open_time time without time zone NOT NULL,
    close_time time without time zone NOT NULL,
    status public.branch_status DEFAULT 'ACTIVE'::public.branch_status NOT NULL,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    updated_at timestamp with time zone DEFAULT now() NOT NULL,
    CONSTRAINT chk_branches_valid_hours CHECK ((open_time < close_time))
);


ALTER TABLE public.branches OWNER TO neondb_owner;

--
-- Name: cancel_policies; Type: TABLE; Schema: public; Owner: neondb_owner
--

CREATE TABLE public.cancel_policies (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    hours_before integer NOT NULL,
    refund_percent numeric(5,2) NOT NULL,
    description text,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    updated_at timestamp with time zone DEFAULT now() NOT NULL,
    CONSTRAINT chk_cancel_policies_hours_positive CHECK ((hours_before >= 0)),
    CONSTRAINT chk_cancel_policies_refund_range CHECK (((refund_percent >= (0)::numeric) AND (refund_percent <= (100)::numeric)))
);


ALTER TABLE public.cancel_policies OWNER TO neondb_owner;

--
-- Name: court_types; Type: TABLE; Schema: public; Owner: neondb_owner
--

CREATE TABLE public.court_types (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    name character varying(255) NOT NULL,
    description text,
    status public.court_type_status DEFAULT 'ACTIVE'::public.court_type_status NOT NULL,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    updated_at timestamp with time zone DEFAULT now() NOT NULL
);


ALTER TABLE public.court_types OWNER TO neondb_owner;

--
-- Name: courts; Type: TABLE; Schema: public; Owner: neondb_owner
--

CREATE TABLE public.courts (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    branch_id uuid NOT NULL,
    court_type_id uuid NOT NULL,
    name character varying(100) NOT NULL,
    description text,
    avatar_url text,
    status public.court_status DEFAULT 'AVAILABLE'::public.court_status NOT NULL,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    updated_at timestamp with time zone DEFAULT now() NOT NULL
);


ALTER TABLE public.courts OWNER TO neondb_owner;

--
-- Name: customer_loyalty; Type: TABLE; Schema: public; Owner: neondb_owner
--

CREATE TABLE public.customer_loyalty (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    user_id uuid NOT NULL,
    tier_id uuid NOT NULL,
    total_points integer DEFAULT 0 NOT NULL,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    updated_at timestamp with time zone DEFAULT now() NOT NULL,
    CONSTRAINT chk_customer_loyalty_points CHECK ((total_points >= 0))
);


ALTER TABLE public.customer_loyalty OWNER TO neondb_owner;

--
-- Name: invoice_code_seq; Type: SEQUENCE; Schema: public; Owner: neondb_owner
--

CREATE SEQUENCE public.invoice_code_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


ALTER SEQUENCE public.invoice_code_seq OWNER TO neondb_owner;

--
-- Name: invoices; Type: TABLE; Schema: public; Owner: neondb_owner
--

CREATE TABLE public.invoices (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    booking_id uuid NOT NULL,
    court_fee numeric(12,2) NOT NULL,
    service_fee numeric(12,2) DEFAULT 0 NOT NULL,
    loyalty_discount_amount numeric(12,2) DEFAULT 0 NOT NULL,
    promotion_discount_amount numeric(12,2) DEFAULT 0 NOT NULL,
    final_total numeric(12,2) NOT NULL,
    payment_status public.invoice_payment_status DEFAULT 'UNPAID'::public.invoice_payment_status NOT NULL,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    updated_at timestamp with time zone DEFAULT now() NOT NULL,
    payment_timing integer DEFAULT 1 NOT NULL,
    invoice_code character varying(20) NOT NULL,
    expires_at timestamp with time zone,
    deposit_rate numeric(5,4),
    deposit_amount numeric(12,2),
    deposit_source public.deposit_source,
    hold_deadline timestamp with time zone,
    CONSTRAINT chk_invoices_amounts_positive CHECK (((court_fee >= (0)::numeric) AND (service_fee >= (0)::numeric) AND (loyalty_discount_amount >= (0)::numeric) AND (promotion_discount_amount >= (0)::numeric) AND (final_total >= (0)::numeric))),
    CONSTRAINT chk_invoices_deposit_amount_positive CHECK (((deposit_amount IS NULL) OR (deposit_amount >= (0)::numeric))),
    CONSTRAINT chk_invoices_deposit_fields CHECK ((((deposit_source IS NULL) = (deposit_amount IS NULL)) AND ((deposit_rate IS NULL) = (deposit_amount IS NULL)))),
    CONSTRAINT chk_invoices_deposit_rate_range CHECK (((deposit_rate IS NULL) OR ((deposit_rate >= (0)::numeric) AND (deposit_rate <= (1)::numeric)))),
    CONSTRAINT chk_invoices_expires_at CHECK (((expires_at IS NULL) OR (expires_at > created_at))),
    CONSTRAINT chk_invoices_final_total_formula CHECK ((final_total = (((court_fee - loyalty_discount_amount) - promotion_discount_amount) + service_fee)))
);


ALTER TABLE public.invoices OWNER TO neondb_owner;

--
-- Name: loyalty_tiers; Type: TABLE; Schema: public; Owner: neondb_owner
--

CREATE TABLE public.loyalty_tiers (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    name character varying(100) NOT NULL,
    min_points integer NOT NULL,
    discount_rate numeric(5,2) DEFAULT 0 NOT NULL,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    updated_at timestamp with time zone DEFAULT now() NOT NULL,
    CONSTRAINT chk_loyalty_tiers_discount_range CHECK (((discount_rate >= (0)::numeric) AND (discount_rate <= (100)::numeric))),
    CONSTRAINT chk_loyalty_tiers_min_points CHECK ((min_points >= 0))
);


ALTER TABLE public.loyalty_tiers OWNER TO neondb_owner;

--
-- Name: loyalty_transactions; Type: TABLE; Schema: public; Owner: neondb_owner
--

CREATE TABLE public.loyalty_transactions (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    user_id uuid NOT NULL,
    booking_id uuid,
    points integer NOT NULL,
    total_points_after integer NOT NULL,
    type public.loyalty_transaction_type NOT NULL,
    note text,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    CONSTRAINT chk_loyalty_earn_booking_id CHECK ((((type = 'EARN'::public.loyalty_transaction_type) AND (booking_id IS NOT NULL)) OR (type = 'ADJUST'::public.loyalty_transaction_type))),
    CONSTRAINT chk_loyalty_transactions_adjust_note CHECK ((((type = 'ADJUST'::public.loyalty_transaction_type) AND (note IS NOT NULL)) OR (type = 'EARN'::public.loyalty_transaction_type))),
    CONSTRAINT chk_loyalty_transactions_points_nonzero CHECK ((points <> 0))
);


ALTER TABLE public.loyalty_transactions OWNER TO neondb_owner;

--
-- Name: oauth_accounts; Type: TABLE; Schema: public; Owner: neondb_owner
--

CREATE TABLE public.oauth_accounts (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    user_id uuid NOT NULL,
    provider character varying(20) NOT NULL,
    provider_user_id character varying(255) NOT NULL,
    created_at timestamp with time zone DEFAULT now() NOT NULL
);


ALTER TABLE public.oauth_accounts OWNER TO neondb_owner;

--
-- Name: otp_codes; Type: TABLE; Schema: public; Owner: neondb_owner
--

CREATE TABLE public.otp_codes (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    user_id uuid NOT NULL,
    type public.otp_type NOT NULL,
    code_hash character varying(255) NOT NULL,
    attempt_count integer DEFAULT 0 NOT NULL,
    expires_at timestamp with time zone NOT NULL,
    used_at timestamp with time zone,
    created_at timestamp with time zone DEFAULT now() NOT NULL
);


ALTER TABLE public.otp_codes OWNER TO neondb_owner;

--
-- Name: payment_ipn_logs; Type: TABLE; Schema: public; Owner: neondb_owner
--

CREATE TABLE public.payment_ipn_logs (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    payment_id uuid,
    provider public.ipn_provider NOT NULL,
    provider_transaction_id character varying(255),
    raw_payload text NOT NULL,
    is_valid boolean NOT NULL,
    processed_at timestamp with time zone DEFAULT now() NOT NULL
);


ALTER TABLE public.payment_ipn_logs OWNER TO neondb_owner;

--
-- Name: payments; Type: TABLE; Schema: public; Owner: neondb_owner
--

CREATE TABLE public.payments (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    invoice_id uuid NOT NULL,
    method public.payment_tx_method NOT NULL,
    amount numeric(12,2) NOT NULL,
    refunded_amount numeric(12,2) DEFAULT 0 NOT NULL,
    status public.payment_tx_status DEFAULT 'PENDING'::public.payment_tx_status NOT NULL,
    transaction_ref character varying(255),
    paid_at timestamp with time zone,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    updated_at timestamp with time zone DEFAULT now() NOT NULL,
    CONSTRAINT chk_payments_amount_positive CHECK ((amount > (0)::numeric)),
    CONSTRAINT chk_payments_paid_at CHECK ((((status = 'SUCCESS'::public.payment_tx_status) AND (paid_at IS NOT NULL)) OR (status <> 'SUCCESS'::public.payment_tx_status))),
    CONSTRAINT chk_payments_refunded_range CHECK (((refunded_amount >= (0)::numeric) AND (refunded_amount <= amount)))
);


ALTER TABLE public.payments OWNER TO neondb_owner;

--
-- Name: promotion_conditions; Type: TABLE; Schema: public; Owner: neondb_owner
--

CREATE TABLE public.promotion_conditions (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    promotion_id uuid NOT NULL,
    condition_type character varying(50) NOT NULL,
    condition_value character varying(200) NOT NULL
);


ALTER TABLE public.promotion_conditions OWNER TO neondb_owner;

--
-- Name: promotions; Type: TABLE; Schema: public; Owner: neondb_owner
--

CREATE TABLE public.promotions (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    name character varying(255) NOT NULL,
    discount_value numeric(10,2) NOT NULL,
    start_date date NOT NULL,
    end_date date NOT NULL,
    status public.promotion_status DEFAULT 'INACTIVE'::public.promotion_status NOT NULL,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    updated_at timestamp with time zone DEFAULT now() NOT NULL,
    code character varying(50),
    promo_display_url text,
    description character varying(500),
    discount_type public.discount_type_enum DEFAULT 'PERCENT'::public.discount_type_enum NOT NULL,
    max_discount_amount numeric(18,2),
    usage_limit integer,
    usage_per_user_limit integer,
    used_count integer DEFAULT 0 NOT NULL,
    CONSTRAINT chk_promotions_date_range CHECK ((start_date <= end_date)),
    CONSTRAINT chk_promotions_discount_range CHECK ((((discount_type = 'PERCENT'::public.discount_type_enum) AND (discount_value > (0)::numeric) AND (discount_value <= (100)::numeric)) OR ((discount_type = 'FIXED'::public.discount_type_enum) AND (discount_value > (0)::numeric) AND (discount_value <= 99999999.99)))),
    CONSTRAINT chk_promotions_used_count CHECK ((used_count >= 0))
);


ALTER TABLE public.promotions OWNER TO neondb_owner;

--
-- Name: refresh_tokens; Type: TABLE; Schema: public; Owner: neondb_owner
--

CREATE TABLE public.refresh_tokens (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    user_id uuid NOT NULL,
    token_hash character varying(255) NOT NULL,
    rotated_from_id uuid,
    expires_at timestamp with time zone NOT NULL,
    revoked_at timestamp with time zone,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    device_name character varying(200),
    ip_address character varying(45),
    user_agent character varying(500),
    last_used_at timestamp with time zone
);


ALTER TABLE public.refresh_tokens OWNER TO neondb_owner;

--
-- Name: refunds; Type: TABLE; Schema: public; Owner: neondb_owner
--

CREATE TABLE public.refunds (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    payment_id uuid NOT NULL,
    amount numeric(12,2) NOT NULL,
    refund_percent numeric(5,2) NOT NULL,
    status public.refund_status DEFAULT 'PENDING'::public.refund_status NOT NULL,
    processed_by uuid,
    processed_at timestamp with time zone,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    CONSTRAINT chk_refunds_amount_positive CHECK ((amount > (0)::numeric)),
    CONSTRAINT chk_refunds_percent_range CHECK (((refund_percent >= (0)::numeric) AND (refund_percent <= (100)::numeric)))
);


ALTER TABLE public.refunds OWNER TO neondb_owner;

--
-- Name: services; Type: TABLE; Schema: public; Owner: neondb_owner
--

CREATE TABLE public.services (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    name character varying(255) NOT NULL,
    description text,
    unit character varying(50) NOT NULL,
    default_price numeric(12,2) NOT NULL,
    status public.service_status DEFAULT 'ACTIVE'::public.service_status NOT NULL,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    updated_at timestamp with time zone DEFAULT now() NOT NULL,
    service_display_url text,
    CONSTRAINT chk_services_price_positive CHECK ((default_price > (0)::numeric))
);


ALTER TABLE public.services OWNER TO neondb_owner;

--
-- Name: slot_interests; Type: TABLE; Schema: public; Owner: neondb_owner
--

CREATE TABLE public.slot_interests (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    court_id uuid NOT NULL,
    date date NOT NULL,
    start_time time without time zone NOT NULL,
    end_time time without time zone NOT NULL,
    email character varying(255) NOT NULL,
    customer_id uuid,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    expires_at timestamp with time zone NOT NULL,
    CONSTRAINT ck_slot_interests_time_valid CHECK ((start_time < end_time))
);


ALTER TABLE public.slot_interests OWNER TO neondb_owner;

--
-- Name: slot_locks; Type: TABLE; Schema: public; Owner: neondb_owner
--

CREATE TABLE public.slot_locks (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    court_id uuid NOT NULL,
    booking_id uuid NOT NULL,
    session_id character varying(255),
    date date NOT NULL,
    start_time time without time zone NOT NULL,
    end_time time without time zone NOT NULL,
    expires_at timestamp with time zone NOT NULL,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    CONSTRAINT chk_slot_locks_valid_time CHECK ((end_time > start_time))
);


ALTER TABLE public.slot_locks OWNER TO neondb_owner;

--
-- Name: system_prices; Type: TABLE; Schema: public; Owner: neondb_owner
--

CREATE TABLE public.system_prices (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    court_type_id uuid NOT NULL,
    time_slot_id uuid NOT NULL,
    price numeric(12,2) NOT NULL,
    effective_from date NOT NULL,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    CONSTRAINT chk_system_prices_positive CHECK ((price > (0)::numeric))
);


ALTER TABLE public.system_prices OWNER TO neondb_owner;

--
-- Name: time_slots; Type: TABLE; Schema: public; Owner: neondb_owner
--

CREATE TABLE public.time_slots (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    start_time time without time zone NOT NULL,
    end_time time without time zone NOT NULL,
    day_type public.day_type NOT NULL,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    CONSTRAINT chk_time_slots_valid_range CHECK ((start_time < end_time))
);


ALTER TABLE public.time_slots OWNER TO neondb_owner;

--
-- Name: user_branches; Type: TABLE; Schema: public; Owner: neondb_owner
--

CREATE TABLE public.user_branches (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    user_id uuid NOT NULL,
    branch_id uuid NOT NULL,
    role public.user_branch_role NOT NULL,
    is_active boolean DEFAULT true NOT NULL,
    assigned_at timestamp with time zone DEFAULT now() NOT NULL,
    ended_at timestamp with time zone,
    CONSTRAINT chk_user_branches_active_ended CHECK ((((is_active = true) AND (ended_at IS NULL)) OR (is_active = false))),
    CONSTRAINT chk_user_branches_valid_time CHECK (((ended_at IS NULL) OR (ended_at > assigned_at)))
);


ALTER TABLE public.user_branches OWNER TO neondb_owner;

--
-- Name: users; Type: TABLE; Schema: public; Owner: neondb_owner
--

CREATE TABLE public.users (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    email character varying(255) NOT NULL,
    password_hash character varying(255),
    full_name character varying(255) NOT NULL,
    phone character varying(20),
    avatar_url text,
    role public.user_role DEFAULT 'CUSTOMER'::public.user_role NOT NULL,
    status public.user_status DEFAULT 'ACTIVE'::public.user_status NOT NULL,
    lock_reason text,
    is_email_verified boolean DEFAULT false NOT NULL,
    is_2fa_enabled boolean DEFAULT false NOT NULL,
    must_change_password boolean DEFAULT false NOT NULL,
    failed_login_count integer DEFAULT 0 NOT NULL,
    locked_until timestamp with time zone,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    updated_at timestamp with time zone DEFAULT now() NOT NULL,
    last_failed_login_at timestamp with time zone,
    locked_at timestamp with time zone,
    locked_by uuid,
    full_name_normalized text
);


ALTER TABLE public.users OWNER TO neondb_owner;

--
-- Name: aggregatedcounter id; Type: DEFAULT; Schema: hangfire; Owner: neondb_owner
--

ALTER TABLE ONLY hangfire.aggregatedcounter ALTER COLUMN id SET DEFAULT nextval('hangfire.aggregatedcounter_id_seq'::regclass);


--
-- Name: counter id; Type: DEFAULT; Schema: hangfire; Owner: neondb_owner
--

ALTER TABLE ONLY hangfire.counter ALTER COLUMN id SET DEFAULT nextval('hangfire.counter_id_seq'::regclass);


--
-- Name: hash id; Type: DEFAULT; Schema: hangfire; Owner: neondb_owner
--

ALTER TABLE ONLY hangfire.hash ALTER COLUMN id SET DEFAULT nextval('hangfire.hash_id_seq'::regclass);


--
-- Name: job id; Type: DEFAULT; Schema: hangfire; Owner: neondb_owner
--

ALTER TABLE ONLY hangfire.job ALTER COLUMN id SET DEFAULT nextval('hangfire.job_id_seq'::regclass);


--
-- Name: jobparameter id; Type: DEFAULT; Schema: hangfire; Owner: neondb_owner
--

ALTER TABLE ONLY hangfire.jobparameter ALTER COLUMN id SET DEFAULT nextval('hangfire.jobparameter_id_seq'::regclass);


--
-- Name: jobqueue id; Type: DEFAULT; Schema: hangfire; Owner: neondb_owner
--

ALTER TABLE ONLY hangfire.jobqueue ALTER COLUMN id SET DEFAULT nextval('hangfire.jobqueue_id_seq'::regclass);


--
-- Name: list id; Type: DEFAULT; Schema: hangfire; Owner: neondb_owner
--

ALTER TABLE ONLY hangfire.list ALTER COLUMN id SET DEFAULT nextval('hangfire.list_id_seq'::regclass);


--
-- Name: set id; Type: DEFAULT; Schema: hangfire; Owner: neondb_owner
--

ALTER TABLE ONLY hangfire.set ALTER COLUMN id SET DEFAULT nextval('hangfire.set_id_seq'::regclass);


--
-- Name: state id; Type: DEFAULT; Schema: hangfire; Owner: neondb_owner
--

ALTER TABLE ONLY hangfire.state ALTER COLUMN id SET DEFAULT nextval('hangfire.state_id_seq'::regclass);


--
-- Name: aggregatedcounter aggregatedcounter_key_key; Type: CONSTRAINT; Schema: hangfire; Owner: neondb_owner
--

ALTER TABLE ONLY hangfire.aggregatedcounter
    ADD CONSTRAINT aggregatedcounter_key_key UNIQUE (key);


--
-- Name: aggregatedcounter aggregatedcounter_pkey; Type: CONSTRAINT; Schema: hangfire; Owner: neondb_owner
--

ALTER TABLE ONLY hangfire.aggregatedcounter
    ADD CONSTRAINT aggregatedcounter_pkey PRIMARY KEY (id);


--
-- Name: counter counter_pkey; Type: CONSTRAINT; Schema: hangfire; Owner: neondb_owner
--

ALTER TABLE ONLY hangfire.counter
    ADD CONSTRAINT counter_pkey PRIMARY KEY (id);


--
-- Name: hash hash_key_field_key; Type: CONSTRAINT; Schema: hangfire; Owner: neondb_owner
--

ALTER TABLE ONLY hangfire.hash
    ADD CONSTRAINT hash_key_field_key UNIQUE (key, field);


--
-- Name: hash hash_pkey; Type: CONSTRAINT; Schema: hangfire; Owner: neondb_owner
--

ALTER TABLE ONLY hangfire.hash
    ADD CONSTRAINT hash_pkey PRIMARY KEY (id);


--
-- Name: job job_pkey; Type: CONSTRAINT; Schema: hangfire; Owner: neondb_owner
--

ALTER TABLE ONLY hangfire.job
    ADD CONSTRAINT job_pkey PRIMARY KEY (id);


--
-- Name: jobparameter jobparameter_pkey; Type: CONSTRAINT; Schema: hangfire; Owner: neondb_owner
--

ALTER TABLE ONLY hangfire.jobparameter
    ADD CONSTRAINT jobparameter_pkey PRIMARY KEY (id);


--
-- Name: jobqueue jobqueue_pkey; Type: CONSTRAINT; Schema: hangfire; Owner: neondb_owner
--

ALTER TABLE ONLY hangfire.jobqueue
    ADD CONSTRAINT jobqueue_pkey PRIMARY KEY (id);


--
-- Name: list list_pkey; Type: CONSTRAINT; Schema: hangfire; Owner: neondb_owner
--

ALTER TABLE ONLY hangfire.list
    ADD CONSTRAINT list_pkey PRIMARY KEY (id);


--
-- Name: lock lock_resource_key; Type: CONSTRAINT; Schema: hangfire; Owner: neondb_owner
--

ALTER TABLE ONLY hangfire.lock
    ADD CONSTRAINT lock_resource_key UNIQUE (resource);

ALTER TABLE ONLY hangfire.lock REPLICA IDENTITY USING INDEX lock_resource_key;


--
-- Name: schema schema_pkey; Type: CONSTRAINT; Schema: hangfire; Owner: neondb_owner
--

ALTER TABLE ONLY hangfire.schema
    ADD CONSTRAINT schema_pkey PRIMARY KEY (version);


--
-- Name: server server_pkey; Type: CONSTRAINT; Schema: hangfire; Owner: neondb_owner
--

ALTER TABLE ONLY hangfire.server
    ADD CONSTRAINT server_pkey PRIMARY KEY (id);


--
-- Name: set set_key_value_key; Type: CONSTRAINT; Schema: hangfire; Owner: neondb_owner
--

ALTER TABLE ONLY hangfire.set
    ADD CONSTRAINT set_key_value_key UNIQUE (key, value);


--
-- Name: set set_pkey; Type: CONSTRAINT; Schema: hangfire; Owner: neondb_owner
--

ALTER TABLE ONLY hangfire.set
    ADD CONSTRAINT set_pkey PRIMARY KEY (id);


--
-- Name: state state_pkey; Type: CONSTRAINT; Schema: hangfire; Owner: neondb_owner
--

ALTER TABLE ONLY hangfire.state
    ADD CONSTRAINT state_pkey PRIMARY KEY (id);


--
-- Name: booking_courts excl_booking_courts_no_overlap; Type: CONSTRAINT; Schema: public; Owner: neondb_owner
--

ALTER TABLE ONLY public.booking_courts
    ADD CONSTRAINT excl_booking_courts_no_overlap EXCLUDE USING gist (court_id WITH =, date WITH =, tsrange((date + start_time), (date + end_time)) WITH &&) WHERE ((is_active = true));


--
-- Name: slot_locks excl_slot_locks_no_overlap; Type: CONSTRAINT; Schema: public; Owner: neondb_owner
--

ALTER TABLE ONLY public.slot_locks
    ADD CONSTRAINT excl_slot_locks_no_overlap EXCLUDE USING gist (court_id WITH =, date WITH =, tsrange((date + start_time), (date + end_time)) WITH &&);


--
-- Name: booking_courts pk_booking_courts; Type: CONSTRAINT; Schema: public; Owner: neondb_owner
--

ALTER TABLE ONLY public.booking_courts
    ADD CONSTRAINT pk_booking_courts PRIMARY KEY (id);


--
-- Name: booking_price_items pk_booking_price_items; Type: CONSTRAINT; Schema: public; Owner: neondb_owner
--

ALTER TABLE ONLY public.booking_price_items
    ADD CONSTRAINT pk_booking_price_items PRIMARY KEY (id);


--
-- Name: booking_promotions pk_booking_promotions; Type: CONSTRAINT; Schema: public; Owner: neondb_owner
--

ALTER TABLE ONLY public.booking_promotions
    ADD CONSTRAINT pk_booking_promotions PRIMARY KEY (id);


--
-- Name: booking_services pk_booking_services; Type: CONSTRAINT; Schema: public; Owner: neondb_owner
--

ALTER TABLE ONLY public.booking_services
    ADD CONSTRAINT pk_booking_services PRIMARY KEY (id);


--
-- Name: bookings pk_bookings; Type: CONSTRAINT; Schema: public; Owner: neondb_owner
--

ALTER TABLE ONLY public.bookings
    ADD CONSTRAINT pk_bookings PRIMARY KEY (id);


--
-- Name: branch_court_types pk_branch_court_types; Type: CONSTRAINT; Schema: public; Owner: neondb_owner
--

ALTER TABLE ONLY public.branch_court_types
    ADD CONSTRAINT pk_branch_court_types PRIMARY KEY (id);


--
-- Name: branch_price_overrides pk_branch_price_overrides; Type: CONSTRAINT; Schema: public; Owner: neondb_owner
--

ALTER TABLE ONLY public.branch_price_overrides
    ADD CONSTRAINT pk_branch_price_overrides PRIMARY KEY (id);


--
-- Name: branch_services pk_branch_services; Type: CONSTRAINT; Schema: public; Owner: neondb_owner
--

ALTER TABLE ONLY public.branch_services
    ADD CONSTRAINT pk_branch_services PRIMARY KEY (id);


--
-- Name: branches pk_branches; Type: CONSTRAINT; Schema: public; Owner: neondb_owner
--

ALTER TABLE ONLY public.branches
    ADD CONSTRAINT pk_branches PRIMARY KEY (id);


--
-- Name: cancel_policies pk_cancel_policies; Type: CONSTRAINT; Schema: public; Owner: neondb_owner
--

ALTER TABLE ONLY public.cancel_policies
    ADD CONSTRAINT pk_cancel_policies PRIMARY KEY (id);


--
-- Name: court_types pk_court_types; Type: CONSTRAINT; Schema: public; Owner: neondb_owner
--

ALTER TABLE ONLY public.court_types
    ADD CONSTRAINT pk_court_types PRIMARY KEY (id);


--
-- Name: courts pk_courts; Type: CONSTRAINT; Schema: public; Owner: neondb_owner
--

ALTER TABLE ONLY public.courts
    ADD CONSTRAINT pk_courts PRIMARY KEY (id);


--
-- Name: customer_loyalty pk_customer_loyalty; Type: CONSTRAINT; Schema: public; Owner: neondb_owner
--

ALTER TABLE ONLY public.customer_loyalty
    ADD CONSTRAINT pk_customer_loyalty PRIMARY KEY (id);


--
-- Name: invoices pk_invoices; Type: CONSTRAINT; Schema: public; Owner: neondb_owner
--

ALTER TABLE ONLY public.invoices
    ADD CONSTRAINT pk_invoices PRIMARY KEY (id);


--
-- Name: loyalty_tiers pk_loyalty_tiers; Type: CONSTRAINT; Schema: public; Owner: neondb_owner
--

ALTER TABLE ONLY public.loyalty_tiers
    ADD CONSTRAINT pk_loyalty_tiers PRIMARY KEY (id);


--
-- Name: loyalty_transactions pk_loyalty_transactions; Type: CONSTRAINT; Schema: public; Owner: neondb_owner
--

ALTER TABLE ONLY public.loyalty_transactions
    ADD CONSTRAINT pk_loyalty_transactions PRIMARY KEY (id);


--
-- Name: oauth_accounts pk_oauth_accounts; Type: CONSTRAINT; Schema: public; Owner: neondb_owner
--

ALTER TABLE ONLY public.oauth_accounts
    ADD CONSTRAINT pk_oauth_accounts PRIMARY KEY (id);


--
-- Name: otp_codes pk_otp_codes; Type: CONSTRAINT; Schema: public; Owner: neondb_owner
--

ALTER TABLE ONLY public.otp_codes
    ADD CONSTRAINT pk_otp_codes PRIMARY KEY (id);


--
-- Name: payment_ipn_logs pk_payment_ipn_logs; Type: CONSTRAINT; Schema: public; Owner: neondb_owner
--

ALTER TABLE ONLY public.payment_ipn_logs
    ADD CONSTRAINT pk_payment_ipn_logs PRIMARY KEY (id);


--
-- Name: payments pk_payments; Type: CONSTRAINT; Schema: public; Owner: neondb_owner
--

ALTER TABLE ONLY public.payments
    ADD CONSTRAINT pk_payments PRIMARY KEY (id);


--
-- Name: promotions pk_promotions; Type: CONSTRAINT; Schema: public; Owner: neondb_owner
--

ALTER TABLE ONLY public.promotions
    ADD CONSTRAINT pk_promotions PRIMARY KEY (id);


--
-- Name: refresh_tokens pk_refresh_tokens; Type: CONSTRAINT; Schema: public; Owner: neondb_owner
--

ALTER TABLE ONLY public.refresh_tokens
    ADD CONSTRAINT pk_refresh_tokens PRIMARY KEY (id);


--
-- Name: refunds pk_refunds; Type: CONSTRAINT; Schema: public; Owner: neondb_owner
--

ALTER TABLE ONLY public.refunds
    ADD CONSTRAINT pk_refunds PRIMARY KEY (id);


--
-- Name: services pk_services; Type: CONSTRAINT; Schema: public; Owner: neondb_owner
--

ALTER TABLE ONLY public.services
    ADD CONSTRAINT pk_services PRIMARY KEY (id);


--
-- Name: slot_locks pk_slot_locks; Type: CONSTRAINT; Schema: public; Owner: neondb_owner
--

ALTER TABLE ONLY public.slot_locks
    ADD CONSTRAINT pk_slot_locks PRIMARY KEY (id);


--
-- Name: system_prices pk_system_prices; Type: CONSTRAINT; Schema: public; Owner: neondb_owner
--

ALTER TABLE ONLY public.system_prices
    ADD CONSTRAINT pk_system_prices PRIMARY KEY (id);


--
-- Name: time_slots pk_time_slots; Type: CONSTRAINT; Schema: public; Owner: neondb_owner
--

ALTER TABLE ONLY public.time_slots
    ADD CONSTRAINT pk_time_slots PRIMARY KEY (id);


--
-- Name: user_branches pk_user_branches; Type: CONSTRAINT; Schema: public; Owner: neondb_owner
--

ALTER TABLE ONLY public.user_branches
    ADD CONSTRAINT pk_user_branches PRIMARY KEY (id);


--
-- Name: users pk_users; Type: CONSTRAINT; Schema: public; Owner: neondb_owner
--

ALTER TABLE ONLY public.users
    ADD CONSTRAINT pk_users PRIMARY KEY (id);


--
-- Name: promotion_conditions promotion_conditions_pkey; Type: CONSTRAINT; Schema: public; Owner: neondb_owner
--

ALTER TABLE ONLY public.promotion_conditions
    ADD CONSTRAINT promotion_conditions_pkey PRIMARY KEY (id);


--
-- Name: promotions promotions_code_key; Type: CONSTRAINT; Schema: public; Owner: neondb_owner
--

ALTER TABLE ONLY public.promotions
    ADD CONSTRAINT promotions_code_key UNIQUE (code);


--
-- Name: slot_interests slot_interests_pkey; Type: CONSTRAINT; Schema: public; Owner: neondb_owner
--

ALTER TABLE ONLY public.slot_interests
    ADD CONSTRAINT slot_interests_pkey PRIMARY KEY (id);


--
-- Name: booking_promotions uq_booking_promotions_booking; Type: CONSTRAINT; Schema: public; Owner: neondb_owner
--

ALTER TABLE ONLY public.booking_promotions
    ADD CONSTRAINT uq_booking_promotions_booking UNIQUE (booking_id);


--
-- Name: bookings uq_bookings_booking_code; Type: CONSTRAINT; Schema: public; Owner: neondb_owner
--

ALTER TABLE ONLY public.bookings
    ADD CONSTRAINT uq_bookings_booking_code UNIQUE (booking_code);


--
-- Name: branch_court_types uq_branch_court_types; Type: CONSTRAINT; Schema: public; Owner: neondb_owner
--

ALTER TABLE ONLY public.branch_court_types
    ADD CONSTRAINT uq_branch_court_types UNIQUE (branch_id, court_type_id);


--
-- Name: branch_price_overrides uq_branch_price_overrides; Type: CONSTRAINT; Schema: public; Owner: neondb_owner
--

ALTER TABLE ONLY public.branch_price_overrides
    ADD CONSTRAINT uq_branch_price_overrides UNIQUE (branch_id, court_type_id, time_slot_id, effective_from);


--
-- Name: branch_services uq_branch_services; Type: CONSTRAINT; Schema: public; Owner: neondb_owner
--

ALTER TABLE ONLY public.branch_services
    ADD CONSTRAINT uq_branch_services UNIQUE (branch_id, service_id);


--
-- Name: cancel_policies uq_cancel_policies_hours; Type: CONSTRAINT; Schema: public; Owner: neondb_owner
--

ALTER TABLE ONLY public.cancel_policies
    ADD CONSTRAINT uq_cancel_policies_hours UNIQUE (hours_before);


--
-- Name: courts uq_courts_name_per_branch; Type: CONSTRAINT; Schema: public; Owner: neondb_owner
--

ALTER TABLE ONLY public.courts
    ADD CONSTRAINT uq_courts_name_per_branch UNIQUE (branch_id, name);


--
-- Name: customer_loyalty uq_customer_loyalty_user; Type: CONSTRAINT; Schema: public; Owner: neondb_owner
--

ALTER TABLE ONLY public.customer_loyalty
    ADD CONSTRAINT uq_customer_loyalty_user UNIQUE (user_id);


--
-- Name: invoices uq_invoices_booking; Type: CONSTRAINT; Schema: public; Owner: neondb_owner
--

ALTER TABLE ONLY public.invoices
    ADD CONSTRAINT uq_invoices_booking UNIQUE (booking_id);


--
-- Name: invoices uq_invoices_invoice_code; Type: CONSTRAINT; Schema: public; Owner: neondb_owner
--

ALTER TABLE ONLY public.invoices
    ADD CONSTRAINT uq_invoices_invoice_code UNIQUE (invoice_code);


--
-- Name: loyalty_tiers uq_loyalty_tiers_min_points; Type: CONSTRAINT; Schema: public; Owner: neondb_owner
--

ALTER TABLE ONLY public.loyalty_tiers
    ADD CONSTRAINT uq_loyalty_tiers_min_points UNIQUE (min_points);


--
-- Name: loyalty_tiers uq_loyalty_tiers_name; Type: CONSTRAINT; Schema: public; Owner: neondb_owner
--

ALTER TABLE ONLY public.loyalty_tiers
    ADD CONSTRAINT uq_loyalty_tiers_name UNIQUE (name);


--
-- Name: oauth_accounts uq_oauth_accounts_provider; Type: CONSTRAINT; Schema: public; Owner: neondb_owner
--

ALTER TABLE ONLY public.oauth_accounts
    ADD CONSTRAINT uq_oauth_accounts_provider UNIQUE (provider, provider_user_id);


--
-- Name: system_prices uq_system_prices; Type: CONSTRAINT; Schema: public; Owner: neondb_owner
--

ALTER TABLE ONLY public.system_prices
    ADD CONSTRAINT uq_system_prices UNIQUE (court_type_id, time_slot_id, effective_from);


--
-- Name: time_slots uq_time_slots; Type: CONSTRAINT; Schema: public; Owner: neondb_owner
--

ALTER TABLE ONLY public.time_slots
    ADD CONSTRAINT uq_time_slots UNIQUE (start_time, end_time, day_type);


--
-- Name: ix_hangfire_counter_expireat; Type: INDEX; Schema: hangfire; Owner: neondb_owner
--

CREATE INDEX ix_hangfire_counter_expireat ON hangfire.counter USING btree (expireat);


--
-- Name: ix_hangfire_counter_key; Type: INDEX; Schema: hangfire; Owner: neondb_owner
--

CREATE INDEX ix_hangfire_counter_key ON hangfire.counter USING btree (key);


--
-- Name: ix_hangfire_hash_expireat; Type: INDEX; Schema: hangfire; Owner: neondb_owner
--

CREATE INDEX ix_hangfire_hash_expireat ON hangfire.hash USING btree (expireat);


--
-- Name: ix_hangfire_job_expireat; Type: INDEX; Schema: hangfire; Owner: neondb_owner
--

CREATE INDEX ix_hangfire_job_expireat ON hangfire.job USING btree (expireat);


--
-- Name: ix_hangfire_job_statename; Type: INDEX; Schema: hangfire; Owner: neondb_owner
--

CREATE INDEX ix_hangfire_job_statename ON hangfire.job USING btree (statename);


--
-- Name: ix_hangfire_job_statename_is_not_null; Type: INDEX; Schema: hangfire; Owner: neondb_owner
--

CREATE INDEX ix_hangfire_job_statename_is_not_null ON hangfire.job USING btree (statename) INCLUDE (id) WHERE (statename IS NOT NULL);


--
-- Name: ix_hangfire_jobparameter_jobidandname; Type: INDEX; Schema: hangfire; Owner: neondb_owner
--

CREATE INDEX ix_hangfire_jobparameter_jobidandname ON hangfire.jobparameter USING btree (jobid, name);


--
-- Name: ix_hangfire_jobqueue_fetchedat_queue_jobid; Type: INDEX; Schema: hangfire; Owner: neondb_owner
--

CREATE INDEX ix_hangfire_jobqueue_fetchedat_queue_jobid ON hangfire.jobqueue USING btree (fetchedat NULLS FIRST, queue, jobid);


--
-- Name: ix_hangfire_jobqueue_jobidandqueue; Type: INDEX; Schema: hangfire; Owner: neondb_owner
--

CREATE INDEX ix_hangfire_jobqueue_jobidandqueue ON hangfire.jobqueue USING btree (jobid, queue);


--
-- Name: ix_hangfire_jobqueue_queueandfetchedat; Type: INDEX; Schema: hangfire; Owner: neondb_owner
--

CREATE INDEX ix_hangfire_jobqueue_queueandfetchedat ON hangfire.jobqueue USING btree (queue, fetchedat);


--
-- Name: ix_hangfire_list_expireat; Type: INDEX; Schema: hangfire; Owner: neondb_owner
--

CREATE INDEX ix_hangfire_list_expireat ON hangfire.list USING btree (expireat);


--
-- Name: ix_hangfire_set_expireat; Type: INDEX; Schema: hangfire; Owner: neondb_owner
--

CREATE INDEX ix_hangfire_set_expireat ON hangfire.set USING btree (expireat);


--
-- Name: ix_hangfire_set_key_score; Type: INDEX; Schema: hangfire; Owner: neondb_owner
--

CREATE INDEX ix_hangfire_set_key_score ON hangfire.set USING btree (key, score);


--
-- Name: ix_hangfire_state_jobid; Type: INDEX; Schema: hangfire; Owner: neondb_owner
--

CREATE INDEX ix_hangfire_state_jobid ON hangfire.state USING btree (jobid);


--
-- Name: idx_booking_courts_active; Type: INDEX; Schema: public; Owner: neondb_owner
--

CREATE INDEX idx_booking_courts_active ON public.booking_courts USING btree (court_id, date) WHERE (is_active = true);


--
-- Name: idx_booking_courts_booking; Type: INDEX; Schema: public; Owner: neondb_owner
--

CREATE INDEX idx_booking_courts_booking ON public.booking_courts USING btree (booking_id);


--
-- Name: idx_booking_courts_conflict; Type: INDEX; Schema: public; Owner: neondb_owner
--

CREATE INDEX idx_booking_courts_conflict ON public.booking_courts USING btree (court_id, date, start_time, end_time);


--
-- Name: idx_booking_courts_court_date; Type: INDEX; Schema: public; Owner: neondb_owner
--

CREATE INDEX idx_booking_courts_court_date ON public.booking_courts USING btree (court_id, date);


--
-- Name: idx_booking_price_items_court; Type: INDEX; Schema: public; Owner: neondb_owner
--

CREATE INDEX idx_booking_price_items_court ON public.booking_price_items USING btree (booking_court_id);


--
-- Name: idx_booking_promotions_booking_id; Type: INDEX; Schema: public; Owner: neondb_owner
--

CREATE INDEX idx_booking_promotions_booking_id ON public.booking_promotions USING btree (booking_id);


--
-- Name: idx_booking_promotions_promotion; Type: INDEX; Schema: public; Owner: neondb_owner
--

CREATE INDEX idx_booking_promotions_promotion ON public.booking_promotions USING btree (promotion_id);


--
-- Name: idx_booking_promotions_promotion_id; Type: INDEX; Schema: public; Owner: neondb_owner
--

CREATE INDEX idx_booking_promotions_promotion_id ON public.booking_promotions USING btree (promotion_id);


--
-- Name: idx_booking_services_booking; Type: INDEX; Schema: public; Owner: neondb_owner
--

CREATE INDEX idx_booking_services_booking ON public.booking_services USING btree (booking_id);


--
-- Name: idx_booking_status_date; Type: INDEX; Schema: public; Owner: neondb_owner
--

CREATE INDEX idx_booking_status_date ON public.bookings USING btree (status, booking_date) INCLUDE (id, branch_id, created_at);


--
-- Name: idx_bookings_branch_date; Type: INDEX; Schema: public; Owner: neondb_owner
--

CREATE INDEX idx_bookings_branch_date ON public.bookings USING btree (branch_id, booking_date);


--
-- Name: idx_bookings_branch_status; Type: INDEX; Schema: public; Owner: neondb_owner
--

CREATE INDEX idx_bookings_branch_status ON public.bookings USING btree (branch_id, status);


--
-- Name: idx_bookings_customer_date; Type: INDEX; Schema: public; Owner: neondb_owner
--

CREATE INDEX idx_bookings_customer_date ON public.bookings USING btree (customer_id, booking_date);


--
-- Name: idx_bookings_date; Type: INDEX; Schema: public; Owner: neondb_owner
--

CREATE INDEX idx_bookings_date ON public.bookings USING btree (booking_date);


--
-- Name: idx_bookings_outcome_pending; Type: INDEX; Schema: public; Owner: neondb_owner
--

CREATE INDEX idx_bookings_outcome_pending ON public.bookings USING btree (updated_at) WHERE ((tadss_decision_id IS NOT NULL) AND (tadss_outcome_reported_at IS NULL));


--
-- Name: idx_bookings_pending_approval; Type: INDEX; Schema: public; Owner: neondb_owner
--

CREATE INDEX idx_bookings_pending_approval ON public.bookings USING btree (approval_expires_at) WHERE (status = 'PENDING_APPROVAL'::public.booking_status);


--
-- Name: idx_bookings_status_date; Type: INDEX; Schema: public; Owner: neondb_owner
--

CREATE INDEX idx_bookings_status_date ON public.bookings USING btree (status, booking_date);


--
-- Name: idx_branch_court_types_active; Type: INDEX; Schema: public; Owner: neondb_owner
--

CREATE INDEX idx_branch_court_types_active ON public.branch_court_types USING btree (branch_id, is_active);


--
-- Name: idx_branch_price_overrides_lookup; Type: INDEX; Schema: public; Owner: neondb_owner
--

CREATE INDEX idx_branch_price_overrides_lookup ON public.branch_price_overrides USING btree (branch_id, court_type_id, time_slot_id, effective_from DESC);


--
-- Name: idx_branch_services_status; Type: INDEX; Schema: public; Owner: neondb_owner
--

CREATE INDEX idx_branch_services_status ON public.branch_services USING btree (branch_id, status);


--
-- Name: idx_branches_name_active; Type: INDEX; Schema: public; Owner: neondb_owner
--

CREATE UNIQUE INDEX idx_branches_name_active ON public.branches USING btree (name) WHERE (status <> 'INACTIVE'::public.branch_status);


--
-- Name: idx_court_types_name_active; Type: INDEX; Schema: public; Owner: neondb_owner
--

CREATE UNIQUE INDEX idx_court_types_name_active ON public.court_types USING btree (name) WHERE (status = 'ACTIVE'::public.court_type_status);


--
-- Name: idx_court_types_status; Type: INDEX; Schema: public; Owner: neondb_owner
--

CREATE INDEX idx_court_types_status ON public.court_types USING btree (status);


--
-- Name: idx_courts_branch; Type: INDEX; Schema: public; Owner: neondb_owner
--

CREATE INDEX idx_courts_branch ON public.courts USING btree (branch_id);


--
-- Name: idx_courts_branch_status; Type: INDEX; Schema: public; Owner: neondb_owner
--

CREATE INDEX idx_courts_branch_status ON public.courts USING btree (branch_id, status);


--
-- Name: idx_invoices_payment_status; Type: INDEX; Schema: public; Owner: neondb_owner
--

CREATE INDEX idx_invoices_payment_status ON public.invoices USING btree (payment_status);


--
-- Name: idx_loyalty_transactions_user; Type: INDEX; Schema: public; Owner: neondb_owner
--

CREATE INDEX idx_loyalty_transactions_user ON public.loyalty_transactions USING btree (user_id, created_at);


--
-- Name: idx_otp_codes_lookup; Type: INDEX; Schema: public; Owner: neondb_owner
--

CREATE INDEX idx_otp_codes_lookup ON public.otp_codes USING btree (user_id, type, expires_at, used_at);


--
-- Name: idx_payment_ipn_logs_payment; Type: INDEX; Schema: public; Owner: neondb_owner
--

CREATE INDEX idx_payment_ipn_logs_payment ON public.payment_ipn_logs USING btree (payment_id);


--
-- Name: idx_payment_ipn_logs_provider_tx; Type: INDEX; Schema: public; Owner: neondb_owner
--

CREATE INDEX idx_payment_ipn_logs_provider_tx ON public.payment_ipn_logs USING btree (provider_transaction_id);


--
-- Name: idx_payments_invoice; Type: INDEX; Schema: public; Owner: neondb_owner
--

CREATE INDEX idx_payments_invoice ON public.payments USING btree (invoice_id);


--
-- Name: idx_payments_status; Type: INDEX; Schema: public; Owner: neondb_owner
--

CREATE INDEX idx_payments_status ON public.payments USING btree (status);


--
-- Name: idx_payments_transaction_ref; Type: INDEX; Schema: public; Owner: neondb_owner
--

CREATE INDEX idx_payments_transaction_ref ON public.payments USING btree (transaction_ref);


--
-- Name: idx_promotion_conditions_promotion_id; Type: INDEX; Schema: public; Owner: neondb_owner
--

CREATE INDEX idx_promotion_conditions_promotion_id ON public.promotion_conditions USING btree (promotion_id);


--
-- Name: idx_promotions_code; Type: INDEX; Schema: public; Owner: neondb_owner
--

CREATE INDEX idx_promotions_code ON public.promotions USING btree (code);


--
-- Name: idx_promotions_dates; Type: INDEX; Schema: public; Owner: neondb_owner
--

CREATE INDEX idx_promotions_dates ON public.promotions USING btree (start_date, end_date);


--
-- Name: idx_promotions_status; Type: INDEX; Schema: public; Owner: neondb_owner
--

CREATE INDEX idx_promotions_status ON public.promotions USING btree (status);


--
-- Name: idx_promotions_status_dates; Type: INDEX; Schema: public; Owner: neondb_owner
--

CREATE INDEX idx_promotions_status_dates ON public.promotions USING btree (status, start_date, end_date);


--
-- Name: idx_refresh_tokens_user; Type: INDEX; Schema: public; Owner: neondb_owner
--

CREATE INDEX idx_refresh_tokens_user ON public.refresh_tokens USING btree (user_id, revoked_at, expires_at);


--
-- Name: idx_refunds_payment; Type: INDEX; Schema: public; Owner: neondb_owner
--

CREATE INDEX idx_refunds_payment ON public.refunds USING btree (payment_id);


--
-- Name: idx_refunds_status; Type: INDEX; Schema: public; Owner: neondb_owner
--

CREATE INDEX idx_refunds_status ON public.refunds USING btree (status);


--
-- Name: idx_services_name_active; Type: INDEX; Schema: public; Owner: neondb_owner
--

CREATE UNIQUE INDEX idx_services_name_active ON public.services USING btree (name) WHERE (status = 'ACTIVE'::public.service_status);


--
-- Name: idx_services_status; Type: INDEX; Schema: public; Owner: neondb_owner
--

CREATE INDEX idx_services_status ON public.services USING btree (status);


--
-- Name: idx_slot_interests_court_date_slot; Type: INDEX; Schema: public; Owner: neondb_owner
--

CREATE INDEX idx_slot_interests_court_date_slot ON public.slot_interests USING btree (court_id, date, start_time, end_time);


--
-- Name: idx_slot_interests_expires; Type: INDEX; Schema: public; Owner: neondb_owner
--

CREATE INDEX idx_slot_interests_expires ON public.slot_interests USING btree (expires_at);


--
-- Name: idx_slot_locks_conflict; Type: INDEX; Schema: public; Owner: neondb_owner
--

CREATE INDEX idx_slot_locks_conflict ON public.slot_locks USING btree (court_id, date, start_time, end_time);


--
-- Name: idx_slot_locks_court; Type: INDEX; Schema: public; Owner: neondb_owner
--

CREATE INDEX idx_slot_locks_court ON public.slot_locks USING btree (court_id);


--
-- Name: idx_slot_locks_expires; Type: INDEX; Schema: public; Owner: neondb_owner
--

CREATE INDEX idx_slot_locks_expires ON public.slot_locks USING btree (expires_at);


--
-- Name: idx_system_prices_lookup; Type: INDEX; Schema: public; Owner: neondb_owner
--

CREATE INDEX idx_system_prices_lookup ON public.system_prices USING btree (court_type_id, time_slot_id, effective_from DESC);


--
-- Name: idx_user_branches_branch; Type: INDEX; Schema: public; Owner: neondb_owner
--

CREATE INDEX idx_user_branches_branch ON public.user_branches USING btree (branch_id);


--
-- Name: idx_user_branches_one_active_manager_per_branch; Type: INDEX; Schema: public; Owner: neondb_owner
--

CREATE UNIQUE INDEX idx_user_branches_one_active_manager_per_branch ON public.user_branches USING btree (branch_id) WHERE ((is_active = true) AND (role = 'MANAGER'::public.user_branch_role));


--
-- Name: idx_user_branches_one_active_per_user; Type: INDEX; Schema: public; Owner: neondb_owner
--

CREATE UNIQUE INDEX idx_user_branches_one_active_per_user ON public.user_branches USING btree (user_id) WHERE (is_active = true);


--
-- Name: idx_user_branches_user_branch; Type: INDEX; Schema: public; Owner: neondb_owner
--

CREATE INDEX idx_user_branches_user_branch ON public.user_branches USING btree (user_id, branch_id);


--
-- Name: idx_users_locked_at; Type: INDEX; Schema: public; Owner: neondb_owner
--

CREATE INDEX idx_users_locked_at ON public.users USING btree (locked_at);


--
-- Name: idx_users_locked_by; Type: INDEX; Schema: public; Owner: neondb_owner
--

CREATE INDEX idx_users_locked_by ON public.users USING btree (locked_by);


--
-- Name: idx_users_role_status; Type: INDEX; Schema: public; Owner: neondb_owner
--

CREATE INDEX idx_users_role_status ON public.users USING btree (role, status);


--
-- Name: ix_users_full_name_normalized; Type: INDEX; Schema: public; Owner: neondb_owner
--

CREATE INDEX ix_users_full_name_normalized ON public.users USING btree (full_name_normalized);


--
-- Name: uniq_loyalty_booking_earn; Type: INDEX; Schema: public; Owner: neondb_owner
--

CREATE UNIQUE INDEX uniq_loyalty_booking_earn ON public.loyalty_transactions USING btree (booking_id) WHERE (type = 'EARN'::public.loyalty_transaction_type);


--
-- Name: uniq_payments_transaction_ref; Type: INDEX; Schema: public; Owner: neondb_owner
--

CREATE UNIQUE INDEX uniq_payments_transaction_ref ON public.payments USING btree (transaction_ref) WHERE (transaction_ref IS NOT NULL);


--
-- Name: uq_bookings_cancel_token; Type: INDEX; Schema: public; Owner: neondb_owner
--

CREATE UNIQUE INDEX uq_bookings_cancel_token ON public.bookings USING btree (cancel_token_hash) WHERE (cancel_token_hash IS NOT NULL);


--
-- Name: uq_bookings_tadss_decision; Type: INDEX; Schema: public; Owner: neondb_owner
--

CREATE UNIQUE INDEX uq_bookings_tadss_decision ON public.bookings USING btree (tadss_decision_id) WHERE (tadss_decision_id IS NOT NULL);


--
-- Name: uq_refresh_tokens_hash; Type: INDEX; Schema: public; Owner: neondb_owner
--

CREATE UNIQUE INDEX uq_refresh_tokens_hash ON public.refresh_tokens USING btree (token_hash);


--
-- Name: uq_users_email_lower; Type: INDEX; Schema: public; Owner: neondb_owner
--

CREATE UNIQUE INDEX uq_users_email_lower ON public.users USING btree (lower((email)::text));


--
-- Name: ux_booking_service_unique; Type: INDEX; Schema: public; Owner: neondb_owner
--

CREATE UNIQUE INDEX ux_booking_service_unique ON public.booking_services USING btree (booking_id, service_id);


--
-- Name: ux_loyalty_deduct_booking; Type: INDEX; Schema: public; Owner: neondb_owner
--

CREATE UNIQUE INDEX ux_loyalty_deduct_booking ON public.loyalty_transactions USING btree (booking_id) WHERE (type = 'DEDUCT'::public.loyalty_transaction_type);


--
-- Name: ux_slot_interests_unique; Type: INDEX; Schema: public; Owner: neondb_owner
--

CREATE UNIQUE INDEX ux_slot_interests_unique ON public.slot_interests USING btree (court_id, date, start_time, end_time, lower((email)::text));


--
-- Name: jobparameter jobparameter_jobid_fkey; Type: FK CONSTRAINT; Schema: hangfire; Owner: neondb_owner
--

ALTER TABLE ONLY hangfire.jobparameter
    ADD CONSTRAINT jobparameter_jobid_fkey FOREIGN KEY (jobid) REFERENCES hangfire.job(id) ON UPDATE CASCADE ON DELETE CASCADE;


--
-- Name: state state_jobid_fkey; Type: FK CONSTRAINT; Schema: hangfire; Owner: neondb_owner
--

ALTER TABLE ONLY hangfire.state
    ADD CONSTRAINT state_jobid_fkey FOREIGN KEY (jobid) REFERENCES hangfire.job(id) ON UPDATE CASCADE ON DELETE CASCADE;


--
-- Name: booking_courts fk_booking_courts_booking; Type: FK CONSTRAINT; Schema: public; Owner: neondb_owner
--

ALTER TABLE ONLY public.booking_courts
    ADD CONSTRAINT fk_booking_courts_booking FOREIGN KEY (booking_id) REFERENCES public.bookings(id) ON DELETE CASCADE;


--
-- Name: booking_courts fk_booking_courts_court; Type: FK CONSTRAINT; Schema: public; Owner: neondb_owner
--

ALTER TABLE ONLY public.booking_courts
    ADD CONSTRAINT fk_booking_courts_court FOREIGN KEY (court_id) REFERENCES public.courts(id) ON DELETE RESTRICT;


--
-- Name: booking_price_items fk_booking_price_items_booking_court; Type: FK CONSTRAINT; Schema: public; Owner: neondb_owner
--

ALTER TABLE ONLY public.booking_price_items
    ADD CONSTRAINT fk_booking_price_items_booking_court FOREIGN KEY (booking_court_id) REFERENCES public.booking_courts(id) ON DELETE CASCADE;


--
-- Name: booking_price_items fk_booking_price_items_time_slot; Type: FK CONSTRAINT; Schema: public; Owner: neondb_owner
--

ALTER TABLE ONLY public.booking_price_items
    ADD CONSTRAINT fk_booking_price_items_time_slot FOREIGN KEY (time_slot_id) REFERENCES public.time_slots(id) ON DELETE RESTRICT;


--
-- Name: booking_promotions fk_booking_promotions_booking; Type: FK CONSTRAINT; Schema: public; Owner: neondb_owner
--

ALTER TABLE ONLY public.booking_promotions
    ADD CONSTRAINT fk_booking_promotions_booking FOREIGN KEY (booking_id) REFERENCES public.bookings(id) ON DELETE CASCADE;


--
-- Name: booking_promotions fk_booking_promotions_promotion; Type: FK CONSTRAINT; Schema: public; Owner: neondb_owner
--

ALTER TABLE ONLY public.booking_promotions
    ADD CONSTRAINT fk_booking_promotions_promotion FOREIGN KEY (promotion_id) REFERENCES public.promotions(id) ON DELETE RESTRICT;


--
-- Name: booking_services fk_booking_services_booking; Type: FK CONSTRAINT; Schema: public; Owner: neondb_owner
--

ALTER TABLE ONLY public.booking_services
    ADD CONSTRAINT fk_booking_services_booking FOREIGN KEY (booking_id) REFERENCES public.bookings(id) ON DELETE CASCADE;


--
-- Name: booking_services fk_booking_services_service; Type: FK CONSTRAINT; Schema: public; Owner: neondb_owner
--

ALTER TABLE ONLY public.booking_services
    ADD CONSTRAINT fk_booking_services_service FOREIGN KEY (service_id) REFERENCES public.services(id) ON DELETE RESTRICT;


--
-- Name: bookings fk_bookings_approval_resolved_by; Type: FK CONSTRAINT; Schema: public; Owner: neondb_owner
--

ALTER TABLE ONLY public.bookings
    ADD CONSTRAINT fk_bookings_approval_resolved_by FOREIGN KEY (approval_resolved_by) REFERENCES public.users(id) ON DELETE RESTRICT;


--
-- Name: bookings fk_bookings_branch; Type: FK CONSTRAINT; Schema: public; Owner: neondb_owner
--

ALTER TABLE ONLY public.bookings
    ADD CONSTRAINT fk_bookings_branch FOREIGN KEY (branch_id) REFERENCES public.branches(id) ON DELETE RESTRICT;


--
-- Name: bookings fk_bookings_cancelled_by; Type: FK CONSTRAINT; Schema: public; Owner: neondb_owner
--

ALTER TABLE ONLY public.bookings
    ADD CONSTRAINT fk_bookings_cancelled_by FOREIGN KEY (cancelled_by) REFERENCES public.users(id) ON DELETE RESTRICT;


--
-- Name: bookings fk_bookings_created_by; Type: FK CONSTRAINT; Schema: public; Owner: neondb_owner
--

ALTER TABLE ONLY public.bookings
    ADD CONSTRAINT fk_bookings_created_by FOREIGN KEY (created_by) REFERENCES public.users(id) ON DELETE RESTRICT;


--
-- Name: bookings fk_bookings_customer; Type: FK CONSTRAINT; Schema: public; Owner: neondb_owner
--

ALTER TABLE ONLY public.bookings
    ADD CONSTRAINT fk_bookings_customer FOREIGN KEY (customer_id) REFERENCES public.users(id) ON DELETE RESTRICT;


--
-- Name: branch_court_types fk_branch_court_types_branch; Type: FK CONSTRAINT; Schema: public; Owner: neondb_owner
--

ALTER TABLE ONLY public.branch_court_types
    ADD CONSTRAINT fk_branch_court_types_branch FOREIGN KEY (branch_id) REFERENCES public.branches(id) ON DELETE RESTRICT;


--
-- Name: branch_court_types fk_branch_court_types_court_type; Type: FK CONSTRAINT; Schema: public; Owner: neondb_owner
--

ALTER TABLE ONLY public.branch_court_types
    ADD CONSTRAINT fk_branch_court_types_court_type FOREIGN KEY (court_type_id) REFERENCES public.court_types(id) ON DELETE RESTRICT;


--
-- Name: branch_price_overrides fk_branch_price_overrides_branch; Type: FK CONSTRAINT; Schema: public; Owner: neondb_owner
--

ALTER TABLE ONLY public.branch_price_overrides
    ADD CONSTRAINT fk_branch_price_overrides_branch FOREIGN KEY (branch_id) REFERENCES public.branches(id) ON DELETE RESTRICT;


--
-- Name: branch_price_overrides fk_branch_price_overrides_court_type; Type: FK CONSTRAINT; Schema: public; Owner: neondb_owner
--

ALTER TABLE ONLY public.branch_price_overrides
    ADD CONSTRAINT fk_branch_price_overrides_court_type FOREIGN KEY (court_type_id) REFERENCES public.court_types(id) ON DELETE RESTRICT;


--
-- Name: branch_price_overrides fk_branch_price_overrides_time_slot; Type: FK CONSTRAINT; Schema: public; Owner: neondb_owner
--

ALTER TABLE ONLY public.branch_price_overrides
    ADD CONSTRAINT fk_branch_price_overrides_time_slot FOREIGN KEY (time_slot_id) REFERENCES public.time_slots(id) ON DELETE RESTRICT;


--
-- Name: branch_services fk_branch_services_branch; Type: FK CONSTRAINT; Schema: public; Owner: neondb_owner
--

ALTER TABLE ONLY public.branch_services
    ADD CONSTRAINT fk_branch_services_branch FOREIGN KEY (branch_id) REFERENCES public.branches(id) ON DELETE RESTRICT;


--
-- Name: branch_services fk_branch_services_service; Type: FK CONSTRAINT; Schema: public; Owner: neondb_owner
--

ALTER TABLE ONLY public.branch_services
    ADD CONSTRAINT fk_branch_services_service FOREIGN KEY (service_id) REFERENCES public.services(id) ON DELETE RESTRICT;


--
-- Name: courts fk_courts_branch; Type: FK CONSTRAINT; Schema: public; Owner: neondb_owner
--

ALTER TABLE ONLY public.courts
    ADD CONSTRAINT fk_courts_branch FOREIGN KEY (branch_id) REFERENCES public.branches(id) ON DELETE RESTRICT;


--
-- Name: courts fk_courts_court_type; Type: FK CONSTRAINT; Schema: public; Owner: neondb_owner
--

ALTER TABLE ONLY public.courts
    ADD CONSTRAINT fk_courts_court_type FOREIGN KEY (court_type_id) REFERENCES public.court_types(id) ON DELETE RESTRICT;


--
-- Name: customer_loyalty fk_customer_loyalty_tier; Type: FK CONSTRAINT; Schema: public; Owner: neondb_owner
--

ALTER TABLE ONLY public.customer_loyalty
    ADD CONSTRAINT fk_customer_loyalty_tier FOREIGN KEY (tier_id) REFERENCES public.loyalty_tiers(id) ON DELETE RESTRICT;


--
-- Name: customer_loyalty fk_customer_loyalty_user; Type: FK CONSTRAINT; Schema: public; Owner: neondb_owner
--

ALTER TABLE ONLY public.customer_loyalty
    ADD CONSTRAINT fk_customer_loyalty_user FOREIGN KEY (user_id) REFERENCES public.users(id) ON DELETE RESTRICT;


--
-- Name: invoices fk_invoices_booking; Type: FK CONSTRAINT; Schema: public; Owner: neondb_owner
--

ALTER TABLE ONLY public.invoices
    ADD CONSTRAINT fk_invoices_booking FOREIGN KEY (booking_id) REFERENCES public.bookings(id) ON DELETE RESTRICT;


--
-- Name: loyalty_transactions fk_loyalty_transactions_booking; Type: FK CONSTRAINT; Schema: public; Owner: neondb_owner
--

ALTER TABLE ONLY public.loyalty_transactions
    ADD CONSTRAINT fk_loyalty_transactions_booking FOREIGN KEY (booking_id) REFERENCES public.bookings(id) ON DELETE RESTRICT;


--
-- Name: loyalty_transactions fk_loyalty_transactions_user; Type: FK CONSTRAINT; Schema: public; Owner: neondb_owner
--

ALTER TABLE ONLY public.loyalty_transactions
    ADD CONSTRAINT fk_loyalty_transactions_user FOREIGN KEY (user_id) REFERENCES public.users(id) ON DELETE RESTRICT;


--
-- Name: oauth_accounts fk_oauth_accounts_user; Type: FK CONSTRAINT; Schema: public; Owner: neondb_owner
--

ALTER TABLE ONLY public.oauth_accounts
    ADD CONSTRAINT fk_oauth_accounts_user FOREIGN KEY (user_id) REFERENCES public.users(id) ON DELETE CASCADE;


--
-- Name: otp_codes fk_otp_codes_user; Type: FK CONSTRAINT; Schema: public; Owner: neondb_owner
--

ALTER TABLE ONLY public.otp_codes
    ADD CONSTRAINT fk_otp_codes_user FOREIGN KEY (user_id) REFERENCES public.users(id) ON DELETE CASCADE;


--
-- Name: payment_ipn_logs fk_payment_ipn_logs_payment; Type: FK CONSTRAINT; Schema: public; Owner: neondb_owner
--

ALTER TABLE ONLY public.payment_ipn_logs
    ADD CONSTRAINT fk_payment_ipn_logs_payment FOREIGN KEY (payment_id) REFERENCES public.payments(id) ON DELETE RESTRICT;


--
-- Name: payments fk_payments_invoice; Type: FK CONSTRAINT; Schema: public; Owner: neondb_owner
--

ALTER TABLE ONLY public.payments
    ADD CONSTRAINT fk_payments_invoice FOREIGN KEY (invoice_id) REFERENCES public.invoices(id) ON DELETE RESTRICT;


--
-- Name: promotion_conditions fk_promotion_conditions; Type: FK CONSTRAINT; Schema: public; Owner: neondb_owner
--

ALTER TABLE ONLY public.promotion_conditions
    ADD CONSTRAINT fk_promotion_conditions FOREIGN KEY (promotion_id) REFERENCES public.promotions(id) ON DELETE CASCADE;


--
-- Name: refresh_tokens fk_refresh_tokens_rotated_from; Type: FK CONSTRAINT; Schema: public; Owner: neondb_owner
--

ALTER TABLE ONLY public.refresh_tokens
    ADD CONSTRAINT fk_refresh_tokens_rotated_from FOREIGN KEY (rotated_from_id) REFERENCES public.refresh_tokens(id) ON DELETE SET NULL;


--
-- Name: refresh_tokens fk_refresh_tokens_user; Type: FK CONSTRAINT; Schema: public; Owner: neondb_owner
--

ALTER TABLE ONLY public.refresh_tokens
    ADD CONSTRAINT fk_refresh_tokens_user FOREIGN KEY (user_id) REFERENCES public.users(id) ON DELETE CASCADE;


--
-- Name: refunds fk_refunds_payment; Type: FK CONSTRAINT; Schema: public; Owner: neondb_owner
--

ALTER TABLE ONLY public.refunds
    ADD CONSTRAINT fk_refunds_payment FOREIGN KEY (payment_id) REFERENCES public.payments(id) ON DELETE RESTRICT;


--
-- Name: refunds fk_refunds_processed_by; Type: FK CONSTRAINT; Schema: public; Owner: neondb_owner
--

ALTER TABLE ONLY public.refunds
    ADD CONSTRAINT fk_refunds_processed_by FOREIGN KEY (processed_by) REFERENCES public.users(id) ON DELETE RESTRICT;


--
-- Name: slot_interests fk_slot_interests_court; Type: FK CONSTRAINT; Schema: public; Owner: neondb_owner
--

ALTER TABLE ONLY public.slot_interests
    ADD CONSTRAINT fk_slot_interests_court FOREIGN KEY (court_id) REFERENCES public.courts(id) ON DELETE CASCADE;


--
-- Name: slot_interests fk_slot_interests_customer; Type: FK CONSTRAINT; Schema: public; Owner: neondb_owner
--

ALTER TABLE ONLY public.slot_interests
    ADD CONSTRAINT fk_slot_interests_customer FOREIGN KEY (customer_id) REFERENCES public.users(id) ON DELETE SET NULL;


--
-- Name: slot_locks fk_slot_locks_booking; Type: FK CONSTRAINT; Schema: public; Owner: neondb_owner
--

ALTER TABLE ONLY public.slot_locks
    ADD CONSTRAINT fk_slot_locks_booking FOREIGN KEY (booking_id) REFERENCES public.bookings(id) ON DELETE CASCADE;


--
-- Name: slot_locks fk_slot_locks_court; Type: FK CONSTRAINT; Schema: public; Owner: neondb_owner
--

ALTER TABLE ONLY public.slot_locks
    ADD CONSTRAINT fk_slot_locks_court FOREIGN KEY (court_id) REFERENCES public.courts(id) ON DELETE CASCADE;


--
-- Name: system_prices fk_system_prices_court_type; Type: FK CONSTRAINT; Schema: public; Owner: neondb_owner
--

ALTER TABLE ONLY public.system_prices
    ADD CONSTRAINT fk_system_prices_court_type FOREIGN KEY (court_type_id) REFERENCES public.court_types(id) ON DELETE RESTRICT;


--
-- Name: system_prices fk_system_prices_time_slot; Type: FK CONSTRAINT; Schema: public; Owner: neondb_owner
--

ALTER TABLE ONLY public.system_prices
    ADD CONSTRAINT fk_system_prices_time_slot FOREIGN KEY (time_slot_id) REFERENCES public.time_slots(id) ON DELETE RESTRICT;


--
-- Name: user_branches fk_user_branches_branch; Type: FK CONSTRAINT; Schema: public; Owner: neondb_owner
--

ALTER TABLE ONLY public.user_branches
    ADD CONSTRAINT fk_user_branches_branch FOREIGN KEY (branch_id) REFERENCES public.branches(id) ON DELETE RESTRICT;


--
-- Name: user_branches fk_user_branches_user; Type: FK CONSTRAINT; Schema: public; Owner: neondb_owner
--

ALTER TABLE ONLY public.user_branches
    ADD CONSTRAINT fk_user_branches_user FOREIGN KEY (user_id) REFERENCES public.users(id) ON DELETE RESTRICT;


--
-- Name: users fk_users_locked_by; Type: FK CONSTRAINT; Schema: public; Owner: neondb_owner
--

ALTER TABLE ONLY public.users
    ADD CONSTRAINT fk_users_locked_by FOREIGN KEY (locked_by) REFERENCES public.users(id) ON DELETE SET NULL;


--
-- PostgreSQL database dump complete
--

\unrestrict lu8vQKehY1DdNFTcqaIqN1F5Q8BMfnOcv4o1K7RbHL9c7bP5aEfCQfBchIMTRCa

