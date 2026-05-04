"""client automation commercialization shell

Revision ID: 0002_client_automation
Revises: 0001_initial
Create Date: 2026-04-27
"""

from alembic import op
import sqlalchemy as sa

revision = "0002_client_automation"
down_revision = "0001_initial"
branch_labels = None
depends_on = None


def upgrade() -> None:
    op.create_table(
        "licenses",
        sa.Column("id", sa.String(36), primary_key=True),
        sa.Column("user_id", sa.String(36), sa.ForeignKey("users.id"), nullable=False),
        sa.Column("license_prefix", sa.String(32), nullable=False),
        sa.Column("license_hash", sa.String(128), nullable=False, unique=True),
        sa.Column("status", sa.String(32), nullable=False, server_default="active"),
        sa.Column("max_devices", sa.Integer(), nullable=False, server_default="1"),
        sa.Column("created_at", sa.DateTime(), nullable=False, server_default=sa.text("now()")),
        sa.Column("revoked_at", sa.DateTime(), nullable=True),
    )
    op.create_index("ix_licenses_user_id", "licenses", ["user_id"])
    op.create_index("ix_licenses_license_prefix", "licenses", ["license_prefix"])
    op.create_index("ix_licenses_license_hash", "licenses", ["license_hash"])

    op.create_table(
        "devices",
        sa.Column("id", sa.String(36), primary_key=True),
        sa.Column("user_id", sa.String(36), sa.ForeignKey("users.id"), nullable=False),
        sa.Column("license_id", sa.String(36), sa.ForeignKey("licenses.id"), nullable=False),
        sa.Column("device_fingerprint", sa.String(128), nullable=False),
        sa.Column("device_name", sa.String(128), nullable=True),
        sa.Column("app_version", sa.String(64), nullable=False),
        sa.Column("status", sa.String(32), nullable=False, server_default="active"),
        sa.Column("last_seen_at", sa.DateTime(), nullable=False, server_default=sa.text("now()")),
        sa.Column("created_at", sa.DateTime(), nullable=False, server_default=sa.text("now()")),
        sa.UniqueConstraint("license_id", "device_fingerprint", name="uq_devices_license_fingerprint"),
    )
    op.create_index("ix_devices_user_id", "devices", ["user_id"])
    op.create_index("ix_devices_license_id", "devices", ["license_id"])
    op.create_index("ix_devices_device_fingerprint", "devices", ["device_fingerprint"])

    op.create_table(
        "automation_runs",
        sa.Column("id", sa.String(36), primary_key=True),
        sa.Column("user_id", sa.String(36), sa.ForeignKey("users.id"), nullable=False),
        sa.Column("license_id", sa.String(36), sa.ForeignKey("licenses.id"), nullable=False),
        sa.Column("device_id", sa.String(36), sa.ForeignKey("devices.id"), nullable=False),
        sa.Column("workflow", sa.String(128), nullable=False),
        sa.Column("status", sa.String(32), nullable=False, server_default="running"),
        sa.Column("estimated_items", sa.Integer(), nullable=False, server_default="0"),
        sa.Column("processed_items", sa.Integer(), nullable=False, server_default="0"),
        sa.Column("failed_items", sa.Integer(), nullable=False, server_default="0"),
        sa.Column("started_at", sa.DateTime(), nullable=False, server_default=sa.text("now()")),
        sa.Column("heartbeat_at", sa.DateTime(), nullable=False, server_default=sa.text("now()")),
        sa.Column("completed_at", sa.DateTime(), nullable=True),
    )
    op.create_index("ix_automation_runs_user_id", "automation_runs", ["user_id"])
    op.create_index("ix_automation_runs_license_id", "automation_runs", ["license_id"])
    op.create_index("ix_automation_runs_device_id", "automation_runs", ["device_id"])
    op.create_index("ix_automation_runs_workflow", "automation_runs", ["workflow"])

    op.create_table(
        "action_permits",
        sa.Column("id", sa.String(36), primary_key=True),
        sa.Column("run_id", sa.String(36), sa.ForeignKey("automation_runs.id"), nullable=False),
        sa.Column("user_id", sa.String(36), sa.ForeignKey("users.id"), nullable=False),
        sa.Column("action", sa.String(128), nullable=False),
        sa.Column("idempotency_key", sa.String(255), nullable=False),
        sa.Column("units", sa.Integer(), nullable=False, server_default="1"),
        sa.Column("status", sa.String(32), nullable=False, server_default="reserved"),
        sa.Column("created_at", sa.DateTime(), nullable=False, server_default=sa.text("now()")),
        sa.Column("completed_at", sa.DateTime(), nullable=True),
        sa.UniqueConstraint("run_id", "idempotency_key", name="uq_action_permits_run_idempotency"),
    )
    op.create_index("ix_action_permits_run_id", "action_permits", ["run_id"])
    op.create_index("ix_action_permits_user_id", "action_permits", ["user_id"])
    op.create_index("ix_action_permits_action", "action_permits", ["action"])


def downgrade() -> None:
    op.drop_table("action_permits")
    op.drop_table("automation_runs")
    op.drop_table("devices")
    op.drop_table("licenses")
