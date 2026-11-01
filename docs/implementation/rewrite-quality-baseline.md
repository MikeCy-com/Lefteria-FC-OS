# Existing application is the minimum baseline

Accepted user requirement, 2026-11-01: every rewritten component must preserve
the original application's useful functionality and improve its usability,
navigation, maintainability and reliability. This applies to both public/mobile
interfaces and the administration backend. Replacing a component with a reduced
screen does not meet the requirement.

The current original GitHub baseline is
`MikeCy-com/Lefteria-FC@380b4022f0ab0db95fbc52a8613711160a5037a5`.
Its public site was also inspected live at `https://lefteriafc.cy/`.
The protected data snapshot has its own earlier provenance; code inspection
does not relabel or update that snapshot. The live application remains unchanged.

## Completion evidence for each module

1. Map original components, routes, query/hash aliases, API calls, records,
   permissions and real user actions before implementation.
2. Preserve complete page flows: list to detail, tabs/submenus, return location,
   filters, selected organization/team/player and browser back/forward behavior.
3. Compare typography, spacing, responsive layout and interaction with the
   original. Public website parity comes before visual refinements. The admin
   refactor should simplify task grouping and consistent navigation rather than
   reproduce confusing component boundaries.
4. Complete the actual read/write/upload/download actions used by that workflow.
   Use current data and explicit unavailable/empty states; a heading, disabled
   substitute, invented statistic or local mock is not functional completion.
5. Enforce current authorization and ownership on the server. Test revoked
   sessions, cross-organization access, validation and concurrent changes.
   Retain source evidence, audit relevant changes atomically and verify recovery.
6. Run relevant real database/API checks and representative browser journeys.
   Record observed results, remaining differences and deployment status.
7. Publish reviewed code to the rewrite repository and verify the isolated
   preview. A production switch remains a separate approval.

## Required coverage

The user's scope explicitly includes players, documents, statistics, player
evaluations, settings, accounting, marketing and notifications, alongside the
already mapped Academy, staff, training/calendar, fixtures, CMS, shop/customer
flows, media/community and other existing components. Each must use the same
completion evidence. Public, customer, mobile and administrator identity realms
and their permission boundaries remain distinct.

Public projections preserve deliberate anonymous UI fields and source selectors,
not accidental whole-object API exposure. For example, the original Academy UI
abbreviates surnames: the rewrite must do that on the server and exclude full
surnames, birth dates, guardian/contact/medical/account fields from public JSON.
Specific ambiguous records require documented reconciliation; invented blanket
conditions must not silently remove an existing public module.

## Current correction

The first public release omitted original menu groups, submenus, header actions
and page workflows and changed typography/layout. It failed the user's baseline
and is being corrected on `fix/public-website-parity`. The source public shell,
styles and complete route taxonomy now drive that work. The existing preview
must not be described as a completed public-site migration while those gaps
remain.

The separate club-profile editor remains preserved on its own branch while this
public regression is addressed. Its completed source/tests are not a claim that
the broader administration platform has been rewritten.
