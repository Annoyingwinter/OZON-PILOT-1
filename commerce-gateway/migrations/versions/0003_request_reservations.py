"""durable request reservations

Revision ID: 0003_request_reservations
Revises: 0002_client_automation
Create Date: 2026-04-28
"""

from alembic import op
import sqlalchemy as sa

revision = "0003_request_reservations"
down_revision = "0002_client_automation"
branch_labels = None
depends_on = None


def upgrade() -> None:
    op.create_table(
        "request_reservations",
        sa.Column("id", sa.String(64), primary_key=True),
        sa.Column("user_id", sa.String(36), nullable=False),
        sa.Column("api_key_id", sa.String(36), nullable=True),
        sa.Column("subscription_id", sa.String(36), nullable=False),
        sa.Column("period_key", sa.String(64), nullable=False),
        sa.Column("endpoint", sa.String(255), nullable=False),
        sa.Column("units", sa.Integer(), nullable=False, server_default="1"),
        sa.Column("status", sa.String(32), nullable=False, server_default="reserved"),
        sa.Column("created_at", sa.DateTime(), nullable=False, server_default=sa.text("now()")),
        sa.Column("completed_at", sa.DateTime(), nullable=True),
    )
    op.create_index("ix_request_reservations_user_id", "request_reservations", ["user_id"])
    op.create_index("ix_request_reservations_api_key_id", "request_reservations", ["api_key_id"])
    op.create_index("ix_request_reservations_subscription_id", "request_reservations", ["subscription_id"])
    op.create_index("ix_request_reservations_period_key", "request_reservations", ["period_key"])
    op.create_index("ix_request_reservations_status", "request_reservations", ["status"])
    op.create_index("ix_request_reservations_created_at", "request_reservations", ["created_at"])


def downgrade() -> None:
    op.drop_table("request_reservations")
