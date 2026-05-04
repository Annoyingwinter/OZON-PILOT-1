"""admin dashboard hardening indexes and audit truncate guard

Revision ID: 0005_admin_dashboard_hardening
Revises: 0004_action_permit_quota_keys
Create Date: 2026-04-28
"""

from alembic import op

revision = "0005_admin_dashboard_hardening"
down_revision = "0004_action_permit_quota_keys"
branch_labels = None
depends_on = None


def upgrade() -> None:
    op.execute(
        """
        CREATE INDEX IF NOT EXISTS ix_usage_logs_error_ts
        ON usage_logs (ts)
        WHERE error_code IS NOT NULL;
        """
    )
    op.execute(
        """
        CREATE TRIGGER audit_logs_no_truncate
        BEFORE TRUNCATE ON audit_logs
        FOR EACH STATEMENT EXECUTE FUNCTION forbid_audit_log_mutation();
        """
    )


def downgrade() -> None:
    op.execute("DROP TRIGGER IF EXISTS audit_logs_no_truncate ON audit_logs")
    op.execute("DROP INDEX IF EXISTS ix_usage_logs_error_ts")
