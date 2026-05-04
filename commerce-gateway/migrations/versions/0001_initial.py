"""initial commerce gateway schema

Revision ID: 0001_initial
Revises:
Create Date: 2026-04-27
"""

from alembic import op
import sqlalchemy as sa
from sqlalchemy.dialects import postgresql

revision = "0001_initial"
down_revision = None
branch_labels = None
depends_on = None


def upgrade() -> None:
    op.create_table(
        "users",
        sa.Column("id", sa.String(36), primary_key=True),
        sa.Column("email", sa.String(255), nullable=False, unique=True),
        sa.Column("phone", sa.String(64), nullable=True),
        sa.Column("password_hash", sa.String(255), nullable=False),
        sa.Column("role", sa.String(32), nullable=False, server_default="customer"),
        sa.Column("status", sa.String(32), nullable=False, server_default="active"),
        sa.Column("realname_status", sa.String(32), nullable=False, server_default="pending"),
        sa.Column("company_name", sa.String(255), nullable=True),
        sa.Column("tax_id", sa.String(128), nullable=True),
        sa.Column("created_at", sa.DateTime(), nullable=False, server_default=sa.text("now()")),
    )
    op.create_index("ix_users_email", "users", ["email"])

    op.create_table(
        "plans",
        sa.Column("id", sa.String(36), primary_key=True),
        sa.Column("code", sa.String(64), nullable=False, unique=True),
        sa.Column("name", sa.String(128), nullable=False),
        sa.Column("price_cents", sa.Integer(), nullable=False),
        sa.Column("currency", sa.String(16), nullable=False, server_default="CNY"),
        sa.Column("quota_calls", sa.Integer(), nullable=False),
        sa.Column("qps_limit", sa.Integer(), nullable=False),
        sa.Column("features", postgresql.JSONB(astext_type=sa.Text()), nullable=False, server_default="{}"),
        sa.Column("is_active", sa.Boolean(), nullable=False, server_default=sa.text("true")),
        sa.Column("sort_order", sa.Integer(), nullable=False, server_default="100"),
    )

    op.create_table(
        "api_keys",
        sa.Column("id", sa.String(36), primary_key=True),
        sa.Column("user_id", sa.String(36), sa.ForeignKey("users.id"), nullable=False),
        sa.Column("key_prefix", sa.String(32), nullable=False),
        sa.Column("key_hash", sa.String(128), nullable=False, unique=True),
        sa.Column("name", sa.String(128), nullable=False),
        sa.Column("last_used_at", sa.DateTime(), nullable=True),
        sa.Column("revoked_at", sa.DateTime(), nullable=True),
        sa.Column("created_at", sa.DateTime(), nullable=False, server_default=sa.text("now()")),
    )
    op.create_index("ix_api_keys_user_id", "api_keys", ["user_id"])
    op.create_index("ix_api_keys_key_hash", "api_keys", ["key_hash"])
    op.create_index("ix_api_keys_key_prefix", "api_keys", ["key_prefix"])

    op.create_table(
        "subscriptions",
        sa.Column("id", sa.String(36), primary_key=True),
        sa.Column("user_id", sa.String(36), sa.ForeignKey("users.id"), nullable=False),
        sa.Column("plan_id", sa.String(36), sa.ForeignKey("plans.id"), nullable=False),
        sa.Column("status", sa.String(32), nullable=False, server_default="active"),
        sa.Column("current_period_start", sa.DateTime(), nullable=False),
        sa.Column("current_period_end", sa.DateTime(), nullable=False),
        sa.Column("auto_renew", sa.Boolean(), nullable=False, server_default=sa.text("true")),
        sa.Column("canceled_at", sa.DateTime(), nullable=True),
        sa.Column("created_at", sa.DateTime(), nullable=False, server_default=sa.text("now()")),
    )
    op.create_index("ix_subscriptions_user_id", "subscriptions", ["user_id"])
    op.create_index("ix_subscriptions_current_period_end", "subscriptions", ["current_period_end"])

    op.create_table(
        "quotas",
        sa.Column("id", sa.String(36), primary_key=True),
        sa.Column("subscription_id", sa.String(36), sa.ForeignKey("subscriptions.id"), nullable=False),
        sa.Column("period_key", sa.String(64), nullable=False),
        sa.Column("quota_total", sa.Integer(), nullable=False),
        sa.Column("quota_used", sa.Integer(), nullable=False, server_default="0"),
        sa.Column("quota_extra", sa.Integer(), nullable=False, server_default="0"),
        sa.Column("updated_at", sa.DateTime(), nullable=False, server_default=sa.text("now()")),
        sa.UniqueConstraint("subscription_id", "period_key", name="uq_quotas_subscription_period"),
    )
    op.create_index("ix_quotas_subscription_id", "quotas", ["subscription_id"])
    op.create_index("ix_quotas_period_key", "quotas", ["period_key"])

    op.create_table(
        "usage_logs",
        sa.Column("id", sa.Integer(), primary_key=True, autoincrement=True),
        sa.Column("user_id", sa.String(36), nullable=False),
        sa.Column("api_key_id", sa.String(36), nullable=True),
        sa.Column("endpoint", sa.String(255), nullable=False),
        sa.Column("status_code", sa.Integer(), nullable=False),
        sa.Column("latency_ms", sa.Integer(), nullable=False, server_default="0"),
        sa.Column("billable_units", sa.Integer(), nullable=False, server_default="1"),
        sa.Column("request_id", sa.String(64), nullable=False),
        sa.Column("ip", sa.String(64), nullable=True),
        sa.Column("error_code", sa.String(64), nullable=True),
        sa.Column("ts", sa.DateTime(), nullable=False, server_default=sa.text("now()")),
    )
    op.create_index("ix_usage_logs_user_id", "usage_logs", ["user_id"])
    op.create_index("ix_usage_logs_api_key_id", "usage_logs", ["api_key_id"])
    op.create_index("ix_usage_logs_request_id", "usage_logs", ["request_id"])
    op.create_index("ix_usage_logs_endpoint", "usage_logs", ["endpoint"])
    op.create_index("ix_usage_logs_ts", "usage_logs", ["ts"])

    op.create_table(
        "invoices",
        sa.Column("id", sa.String(36), primary_key=True),
        sa.Column("user_id", sa.String(36), sa.ForeignKey("users.id"), nullable=False),
        sa.Column("subscription_id", sa.String(36), nullable=True),
        sa.Column("period_start", sa.DateTime(), nullable=True),
        sa.Column("period_end", sa.DateTime(), nullable=True),
        sa.Column("amount_cents", sa.Integer(), nullable=False),
        sa.Column("status", sa.String(32), nullable=False, server_default="open"),
        sa.Column("payment_method", sa.String(64), nullable=True),
        sa.Column("provider_ref", sa.String(255), nullable=True),
        sa.Column("created_at", sa.DateTime(), nullable=False, server_default=sa.text("now()")),
    )
    op.create_index("ix_invoices_user_id", "invoices", ["user_id"])

    op.create_table(
        "billing_events",
        sa.Column("id", sa.String(36), primary_key=True),
        sa.Column("provider", sa.String(64), nullable=False),
        sa.Column("event_id", sa.String(255), nullable=False),
        sa.Column("event_type", sa.String(128), nullable=False),
        sa.Column("payload", postgresql.JSONB(astext_type=sa.Text()), nullable=False, server_default="{}"),
        sa.Column("processed_at", sa.DateTime(), nullable=True),
        sa.Column("created_at", sa.DateTime(), nullable=False, server_default=sa.text("now()")),
        sa.UniqueConstraint("provider", "event_id", name="uq_billing_events_provider_event"),
    )

    op.create_table(
        "audit_logs",
        sa.Column("id", sa.Integer(), primary_key=True, autoincrement=True),
        sa.Column("actor_type", sa.String(32), nullable=False),
        sa.Column("actor_id", sa.String(64), nullable=False),
        sa.Column("action", sa.String(128), nullable=False),
        sa.Column("target_type", sa.String(64), nullable=False),
        sa.Column("target_id", sa.String(64), nullable=False),
        sa.Column("payload", postgresql.JSONB(astext_type=sa.Text()), nullable=False, server_default="{}"),
        sa.Column("ip", sa.String(64), nullable=True),
        sa.Column("ts", sa.DateTime(), nullable=False, server_default=sa.text("now()")),
    )
    op.create_index("ix_audit_logs_action", "audit_logs", ["action"])
    op.create_index("ix_audit_logs_ts", "audit_logs", ["ts"])

    op.execute(
        """
        CREATE OR REPLACE FUNCTION forbid_audit_log_mutation()
        RETURNS trigger AS $$
        BEGIN
          RAISE EXCEPTION 'audit_logs is append-only';
        END;
        $$ LANGUAGE plpgsql;
        """
    )
    op.execute(
        """
        CREATE TRIGGER audit_logs_append_only
        BEFORE UPDATE OR DELETE ON audit_logs
        FOR EACH ROW EXECUTE FUNCTION forbid_audit_log_mutation();
        """
    )


def downgrade() -> None:
    op.execute("DROP TRIGGER IF EXISTS audit_logs_append_only ON audit_logs")
    op.execute("DROP FUNCTION IF EXISTS forbid_audit_log_mutation")
    op.drop_table("audit_logs")
    op.drop_table("billing_events")
    op.drop_table("invoices")
    op.drop_table("usage_logs")
    op.drop_table("quotas")
    op.drop_table("subscriptions")
    op.drop_table("api_keys")
    op.drop_table("plans")
    op.drop_table("users")
