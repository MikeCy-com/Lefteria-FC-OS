/**
 * HUB editorial state machine — pure application policy, not a persistence or
 * authorization boundary. API handlers must independently verify the current
 * manager identity, organization permission, record version and SQL transaction.
 * AI text and visual assets can be submitted for review but NEVER auto-publish.
 */
export type HubAudience = 'parent' | 'player';
export type HubEditorialStatus = 'ai_draft' | 'review_required' | 'approved' | 'published' | 'rejected';
export type HubActor = { readonly id: string; readonly kind: 'ai' | 'editor' | 'admin' };
export interface HubContentRecord {
  readonly id: string;
  readonly organizationId: string;
  readonly status: HubEditorialStatus;
  readonly version: number;
  readonly title: string;
  readonly audience: readonly HubAudience[];
  readonly textReview: 'pending' | 'passed';
  readonly visualReview: 'pending' | 'passed';
  readonly provenance: 'ai' | 'human';
  readonly approvedBy: string | null;
  readonly publishedAt: string | null;
}
export type HubEditorialAction = 'submit_review' | 'approve' | 'reject' | 'publish' | 'revise';

export function transitionHubContent(
  record: HubContentRecord,
  action: HubEditorialAction,
  actor: HubActor,
  expectedVersion: number,
  now: string
): HubContentRecord {
  if (!actor.id.trim() || !record.id.trim() || !record.organizationId.trim()) throw new Error('invalid_editorial_identity');
  if (record.version !== expectedVersion) throw new Error('editorial_version_conflict');
  if (!Number.isFinite(Date.parse(now))) throw new Error('invalid_editorial_timestamp');
  if (actor.kind === 'ai' && action !== 'submit_review') throw new Error('ai_cannot_approve_or_publish');
  if ((action === 'approve' || action === 'publish' || action === 'reject') && actor.kind !== 'admin')
    throw new Error('admin_approval_required');
  if (action === 'submit_review' && record.status === 'ai_draft' && actor.kind !== 'ai' && actor.kind !== 'editor')
    throw new Error('draft_submit_not_permitted');

  if (action === 'submit_review' && record.status === 'ai_draft')
    return { ...record, status: 'review_required', version: record.version + 1 };
  if (action === 'approve' && record.status === 'review_required') {
    if (record.textReview !== 'passed' || record.visualReview !== 'passed')
      throw new Error('text_and_visual_approval_required');
    if (record.provenance === 'ai' && record.approvedBy === actor.id)
      throw new Error('reviewer_must_not_self_attest_ai_provenance');
    return { ...record, status: 'approved', approvedBy: actor.id, version: record.version + 1 };
  }
  if (action === 'publish' && record.status === 'approved') {
    if (!record.approvedBy || record.textReview !== 'passed' || record.visualReview !== 'passed')
      throw new Error('publication_requires_review');
    return { ...record, status: 'published', publishedAt: now, version: record.version + 1 };
  }
  if (action === 'reject' && record.status === 'review_required')
    return { ...record, status: 'rejected', approvedBy: null, version: record.version + 1 };
  if (action === 'revise' && (record.status === 'rejected' || record.status === 'review_required' || record.status === 'approved') && actor.kind !== 'ai')
    return { ...record, status: 'ai_draft', textReview: 'pending', visualReview: 'pending',
      approvedBy: null, publishedAt: null, version: record.version + 1 };
  throw new Error('invalid_editorial_transition');
}

/** Subscription selection never grants player, parent or family permissions. */
export interface HubFanEntitlement {
  readonly subscriberId: string;
  readonly status: 'pending' | 'active' | 'past_due' | 'cancelled' | 'expired';
  readonly expiresAt: string;
  readonly audiences: readonly HubAudience[];
  readonly confirmedByProvider: boolean;
}

export function canAccessSubscribedHub(
  userId: string,
  role: string,
  entitlement: HubFanEntitlement | null,
  audience: HubAudience,
  now: string
): boolean {
  // Never authorize a blank or whitespace-only identity, even if a provider
  // record happens to contain the same empty subscriber identifier.
  return role === 'fan' && userId.trim().length > 0 && Boolean(
    entitlement && entitlement.subscriberId.trim().length > 0 && entitlement.subscriberId === userId &&
    entitlement.status === 'active' && entitlement.confirmedByProvider &&
    entitlement.audiences.includes(audience) &&
    Number.isFinite(Date.parse(entitlement.expiresAt)) &&
    Number.isFinite(Date.parse(now)) &&
    Date.parse(entitlement.expiresAt) > Date.parse(now)
  );
}
