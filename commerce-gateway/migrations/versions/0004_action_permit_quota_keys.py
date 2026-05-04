"""store quota identity on action permits

Revision ID: 0004_action_permit_quota_keys
Revises: 0003_request_reservations
Create Date: 2026-04-28
"""

from alembic import op
import sqlalchemy as sa

revision = "0004_action_permit_quota_keys"
down_revision = "0003_request_reservations"
branch_labels = None
depends_on = None


def upgrade() -> None:
    op.add_column("action_permits", sa.Column("subscription_id", sa.String(36), nullable=True))
    op.add_column("action_permits", sa.Column("period_key", sa.String(64), nullable=True))
    op.create_index("ix_action_permits_subscription_id", "action_permits", ["subscription_id"])
    op.create_index("ix_action_permits_period_key", "action_permits", ["period_key"])


def downgrade() -> None:
    op.drop_index("ix_action_permits_period_key", table_name="action_permits")
    op.drop_index("ix_action_permits_subscription_id", table_name="action_permits")
    op.drop_column("action_permits", "period_key")
    op.drop_column("action_permits", "subscription_id")
