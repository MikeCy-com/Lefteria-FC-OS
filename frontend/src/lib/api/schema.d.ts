// Generated from contracts/openapi.json. Run npm run api:generate; do not edit.
export interface paths {
    "/api/admin/auth/login": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: operations["LoginWebAdmin"];
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/admin/auth/logout": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: operations["LogoutWebAdmin"];
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/admin/auth/me": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: operations["GetWebAdminSession"];
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/admin/club": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: operations["GetAdminClubProfile"];
        put: operations["UpdateAdminClubProfile"];
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/health/live": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: operations["GetLiveness"];
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/health/ready": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: operations["GetReadiness"];
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/mobile/auth/exchange-legacy": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: operations["ExchangeLegacyMobileSession"];
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/mobile/auth/me": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: operations["GetMobileIdentity"];
        put: operations["UpdateMobileProfilePreference"];
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/mobile/auth/register-fan": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: operations["RegisterMobileFan"];
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/mobile/auth/request-otp": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: operations["RequestMobileOtp"];
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/mobile/auth/switch-role": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: operations["SwitchMobileRole"];
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/mobile/auth/verify-otp": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: operations["VerifyMobileOtp"];
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/mobile/availability": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        /** @description Submits the current parent/player's RSVP. Status is going, not_going or maybe and defaults to going only when omitted. event_type is match, fixture (match alias), event or training; omission requires a unique event ID. Cancelled and archived events reject new RSVPs; past events otherwise remain eligible. Unknown properties are ignored; duplicate JSON keys and bodies over 16 KiB are rejected. */
        post: operations["SubmitMobileAvailability"];
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/mobile/calendar": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** @description Returns a scoped calendar month. month is required in YYYY-MM form; offset is an integer from 0 to 100000. Each page contains at most 200 items. player_id selects an already authorized player and grants no access. */
        get: operations["GetMobileCalendar"];
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/mobile/calendar/{event_type}/{event_id}/responses": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** @description Returns scoped player RSVPs for a currently authorized coach/management identity. These are availability responses, not physical attendance or read receipts. The required event_type is match, fixture, event or training. offset is an integer from 0 to 100000; each page contains at most 200 players. */
        get: operations["GetMobileEventResponses"];
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/mobile/family": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: operations["GetParentFamily"];
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/mobile/family/active-children": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: operations["GetParentActiveFamilyDelegations"];
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/mobile/family/active-delegation": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: operations["GetRecipientFamilyDelegationAvailability"];
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/mobile/family/availability": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        /** @description Submits the current parent/player's RSVP. Status is going, not_going or maybe and defaults to going only when omitted. event_type is match, fixture (match alias), event or training; omission requires a unique event ID. Cancelled and archived events reject new RSVPs; past events otherwise remain eligible. Unknown properties are ignored; duplicate JSON keys and bodies over 16 KiB are rejected. */
        post: operations["SubmitDelegatedFamilyAvailability"];
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/mobile/family/calendar": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** @description Read-only member calendar for the exact player whose accepted family delegation is live at the same SQL statement as event visibility. Requires OTP fan role. No player selector, staff projection, RSVP write, or player-role elevation. */
        get: operations["GetDelegatedFamilyCalendar"];
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/mobile/family/children/{playerId}/invitations": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: operations["IssuePendingFamilyInvitation"];
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/mobile/family/children/{playerId}/revoke": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: operations["RevokeCurrentFamilyPlayerDelegation"];
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/mobile/family/invitations": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: operations["GetRecipientPendingFamilyInvitations"];
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/mobile/family/invitations/{invitationId}/accept": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: operations["AcceptVerifiedRecipientFamilyInvitation"];
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/mobile/family/invitations/{invitationId}/cancel": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: operations["CancelPendingFamilyInvitation"];
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/mobile/family/invitations/{invitationId}/decline": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: operations["DeclineRecipientPendingFamilyInvitation"];
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/mobile/family/outgoing-invitations": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: operations["GetParentOutgoingInvitations"];
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/mobile/management/organization": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: operations["GetMobileOrganization"];
        put?: never;
        post: operations["SelectMobileOrganization"];
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/mobile/my-availability": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** @description Returns RSVP rows only for players currently linked to the authenticated parent/player identity. */
        get: operations["GetMobileOwnAvailability"];
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/mobile/players-list": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: operations["GetMobilePlayersList"];
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/mobile/profile": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put: operations["UpdateMobileProfile"];
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/mobile/profile/avatar": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: operations["UploadMobileAvatar"];
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/public/news": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: operations["GetPublicNews"];
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/public/news/{id}": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: operations["GetPublicNewsArticle"];
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/public/sections": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: operations["GetPublicSections"];
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/public/site": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: operations["GetPublicSite"];
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/uploads/avatars/{fileName}": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: operations["GetMobileAvatar"];
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head: operations["HeadMobileAvatar"];
        patch?: never;
        trace?: never;
    };
}
export type webhooks = Record<string, never>;
export interface components {
    schemas: {
        /** @description Complete safe public club fields only. Null clears optional fields. Unknown keys and duplicate JSON keys are rejected. */
        AdminClubEditable: {
            address: null | string;
            city: null | string;
            country: null | string;
            description: null | string;
            email: null | string;
            /** Format: int32 */
            founded: null | number;
            greek_name: null | string;
            history: null | string;
            logo_url: null | string;
            motto: null | string;
            name: string;
            phone: null | string;
            /** @description Only facebook, instagram, twitter, youtube and tiktok; values are nonempty safe HTTPS URLs. */
            social_links: {
                [key: string]: string;
            };
            stadium_image_url: null | string;
            website: null | string;
        };
        AdminClubProfileDto: {
            club: components["schemas"]["PublicClub"];
            /** Format: int64 */
            version: number;
        };
        AdminClubUpdateCommand: {
            club: components["schemas"]["AdminClubEditable"];
            /** Format: int64 */
            expected_version: number;
        };
        CalendarOwnResponse: {
            player_id: string;
            status: string;
            /** Format: date-time */
            updated_at: null | string;
        };
        CalendarPlayerOption: {
            id: string;
            name: string;
            organization_id: string;
        };
        FamilyChildDto: {
            academy_group_ids: string[];
            id: string;
            name: string;
            organization_id: string;
            team_id: null | string;
            team_type: string;
        };
        FamilyDelegationAvailabilityDto: {
            has_active_delegation: boolean;
        };
        FamilyOperationResult: {
            ok: boolean;
        };
        FanRegistrationBody: {
            birthday?: null | string;
            first_name?: null | string;
            last_name?: null | string;
            name?: null | string;
            phone: null | string;
        };
        FanRegistrationResult: {
            phone: string;
            role_detected: string;
            simulated?: boolean;
        };
        HealthResponse: {
            status: string;
        };
        IssueFamilyInvitationBody: {
            phone: null | string;
        };
        MobileAuthError: {
            code: string;
            detail: string;
        };
        MobileAvailabilityBody: {
            event_id: string;
            event_type?: null | string;
            player_id?: null | string;
            /** @default going */
            status: string;
        };
        MobileAvailabilityResult: {
            event_id: string;
            event_type: string;
            message: string;
            player_id: string;
            status: string;
        };
        MobileAvailabilityRow: {
            event_id: string;
            event_type: string;
            player_id: string;
            status: string;
            /** Format: date-time */
            updated_at: null | string;
        };
        MobileAvatarResult: {
            avatar_url: string;
        };
        MobileAvatarUploadBody: {
            /** Format: binary */
            file: Blob;
        };
        MobileCalendarItem: {
            arrival_time: null | string;
            cancel_reason: null | string;
            content_status: null | string;
            /** Format: date */
            date: string;
            description: null | string;
            /** Format: int32 */
            duration_minutes: null | number | string;
            /** Format: date */
            end_date: string;
            /** Format: date-time */
            ends_at: null | string;
            event_type: string;
            id: string;
            is_cancelled: boolean;
            kind: string;
            location: null | string;
            location_url: null | string;
            member_summary: null | string;
            organization_id: string;
            preparation: null | string;
            responses: components["schemas"]["CalendarOwnResponse"][];
            session_journey: null | string;
            start_time: null | string;
            /** Format: date-time */
            starts_at: null | string;
            status: null | string;
            title: string;
        };
        MobileCalendarResponsePlayer: {
            id: string;
            image_url: null | string;
            name: string;
            /** Format: int32 */
            number: number | string;
            position: string;
            status: null | string;
            /** Format: date-time */
            updated_at: null | string;
        };
        MobileCalendarResponseResult: {
            event_id: string;
            event_type: string;
            /** Format: int32 */
            next_offset: null | number | string;
            organization_id: string;
            players: components["schemas"]["MobileCalendarResponsePlayer"][];
        };
        MobileCalendarResult: {
            /** Format: uuid */
            identity_id: string;
            items: components["schemas"]["MobileCalendarItem"][];
            month: string;
            /** Format: int32 */
            next_offset: null | number | string;
            organization_id: null | string;
            players: components["schemas"]["CalendarPlayerOption"][];
            role: string;
            selected_player_id: null | string;
            time_zone: string;
        };
        MobileIdentityOption: {
            name: string;
            role: string;
        };
        MobileLoginResult: {
            available_roles: string[];
            role: string;
            token: string;
            user: components["schemas"]["MobileUserDto"];
        };
        MobileOrganizationContextDto: {
            available_organizations: components["schemas"]["OrganizationOptionDto"][];
            organization_id: null | string;
            organization_name: null | string;
            requires_organization_selection: boolean;
        };
        MobilePlayerOptionDto: {
            id: string;
            image_url: null | string;
            name: string;
            /** Format: int32 */
            number: number | string;
            position: string;
        };
        MobilePlayersListResult: {
            players: components["schemas"]["MobilePlayerOptionDto"][];
        };
        MobileProfilePreferenceBody: {
            favorite_team?: ("first_team" | "academy" | "both") | null;
        };
        MobileProfilePreferenceResult: {
            ok: boolean;
            user: components["schemas"]["MobileUserDto"];
        };
        /** @description Updates accepted non-null own-profile fields. Null fields are ignored; at least one accepted field is required. Unknown fields are ignored. */
        MobileProfileUpdateBody: {
            birthday?: string | null;
            /** @description A plain email address without a display name; empty string clears it. */
            email?: string | null;
            /** @description Opaque legacy player preference; empty string clears it. This value grants no roster access. */
            favourite_player_id?: string | null;
            first_name?: string | null;
            last_name?: string | null;
            name?: string | null;
            /** @description Replaces the whole preference object; up to 64 keys, each 1..64 characters without controls, with boolean values. Null is ignored; an empty object clears preferences. */
            notification_prefs?: {
                [key: string]: boolean;
            } | null;
        };
        MobileRoleSwitchResult: {
            available_roles?: null | string[];
            noop?: null | boolean;
            role: string;
            token: null | string;
            user?: null | components["schemas"]["MobileUserDto"];
        };
        MobileUserDto: {
            academy_group_ids?: string[];
            available_roles?: string[];
            avatar_url?: null | string;
            birthday?: null | string;
            /** Format: date-time */
            created_at: string;
            current_role_available: boolean;
            email?: null | string;
            favorite_team?: null | string;
            favourite_player_id?: null | string;
            first_name?: null | string;
            /** Format: uuid */
            id: string;
            /** Format: date-time */
            last_login: string;
            last_name?: null | string;
            linked_player_id?: null | string;
            linked_player_ids?: string[];
            name: string;
            notification_prefs?: null | {
                [key: string]: boolean;
            };
            phone: string;
            platform: string;
            role: string;
            team_id?: null | string;
            /** Format: date-time */
            updated_at?: null | string;
        };
        MobileVerificationResult: components["schemas"]["MobileVerificationResultMobileRolePickerResult"] | components["schemas"]["MobileVerificationResultMobileLoginResult"];
        MobileVerificationResultMobileLoginResult: {
            available_roles: string[];
            role: string;
            token: string;
            user: components["schemas"]["MobileUserDto"];
        };
        MobileVerificationResultMobileRolePickerResult: {
            identities: components["schemas"]["MobileIdentityOption"][];
            multi_role?: boolean;
            phone: string;
        };
        OrganizationOptionDto: {
            id: string;
            name: string;
        };
        OrganizationSelectionBody: {
            organization_id: null | string;
        };
        OtpRequestBody: {
            phone: null | string;
        };
        OtpRequestResult: {
            /** Format: int32 */
            expires_in: number | string;
            message: string;
            phone: string;
            role_detected: null | string;
        };
        OtpVerificationBody: {
            code?: null | string;
            phone?: null | string;
            remember_days?: (30 | 60 | 90) | null;
            requested_role?: null | string;
            role?: null | string;
        };
        ParentActiveDelegationDto: {
            organization_id: string;
            player_id: string;
        };
        ParentActiveDelegationsResult: {
            items: components["schemas"]["ParentActiveDelegationDto"][];
        };
        ParentFamilyResult: {
            children: components["schemas"]["FamilyChildDto"][];
        };
        ParentOutgoingInvitationDto: {
            /** Format: date-time */
            created_at: string;
            /** Format: date-time */
            expires_at: string;
            /** Format: uuid */
            id: string;
            organization_id: string;
            player_id: string;
            status: string;
        };
        ParentOutgoingInvitationsResult: {
            items: components["schemas"]["ParentOutgoingInvitationDto"][];
        };
        PendingFamilyInvitationDto: {
            /** Format: date-time */
            created_at: string;
            /** Format: date-time */
            expires_at: string;
            /** Format: uuid */
            id: string;
            status: string;
        };
        PublicAcademyGroup: {
            age_range: null | string;
            banner_url: null | string;
            city: null | string;
            description: null | string;
            /** Format: int32 */
            display_order: number | string;
            id: string;
            location_name: null | string;
            name: string;
            organization_id: string;
            season: null | string;
            training_schedule: null | string;
        };
        PublicArticle: {
            audience: string;
            category: string;
            content: string;
            /** Format: date-time */
            created_at: null | string;
            excerpt: string;
            id: string;
            image_url: null | string;
            is_featured: boolean;
            organization_id: string;
            title: string;
        };
        PublicClub: {
            address: null | string;
            city: null | string;
            country: null | string;
            description: null | string;
            email: null | string;
            /** Format: int32 */
            founded: null | number | string;
            greek_name: null | string;
            history: null | string;
            id: string;
            logo_url: null | string;
            motto: null | string;
            name: string;
            organization_id: string;
            phone: null | string;
            social_links: {
                [key: string]: string;
            };
            stadium_image_url: null | string;
            website: null | string;
        };
        PublicContentError: {
            code: string;
            detail: string;
        };
        PublicFixture: {
            academy_group_id: null | string;
            /** Format: int32 */
            away_score: null | number | string;
            away_team: string;
            away_team_logo: null | string;
            competition: null | string;
            /** Format: int32 */
            home_score: null | number | string;
            home_team: string;
            home_team_logo: null | string;
            id: string;
            /** Format: date */
            match_date: string;
            match_time: null | string;
            organization_id: string;
            status: string;
            team_id: null | string;
            venue: null | string;
        };
        PublicFixtureInstant: {
            id: string;
            /** Format: date-time */
            kickoff_at: null | string;
        };
        PublicNewsResult: {
            items: components["schemas"]["PublicArticle"][];
            /** Format: int32 */
            limit: number | string;
            /** Format: int32 */
            offset: number | string;
            /** Format: int32 */
            total: number | string;
        };
        PublicSectionAcademyStats: {
            /** Format: int32 */
            age_groups: number | string;
            /** Format: int32 */
            athletes: number | string;
            /** Format: int32 */
            dedication_pct: null | number | string;
            id: string;
            /** Format: date-time */
            measured_at: string;
            organization_id: string;
            /** Format: int32 */
            trainings_per_week: null | number | string;
        };
        PublicSectionArchive: {
            /** Format: date-time */
            archived_at: null | string;
            fixtures: components["schemas"]["PublicSectionFixture"][];
            id: string;
            organization_id: string;
            player_stats: components["schemas"]["PublicSectionArchivePlayer"][];
            season_id: string;
            season_name: string;
            standings: components["schemas"]["PublicSectionStanding"][];
            team_type: string;
            /** Format: int32 */
            top_scorer_goals: number | string;
            top_scorer_name: null | string;
        };
        PublicSectionArchivePlayer: {
            image_url: null | string;
            name: string;
            /** Format: int32 */
            number: null | number | string;
            player_id: string;
            statistics: components["schemas"]["PublicSectionStatistics"];
        };
        PublicSectionBenefit: {
            description: null | string;
            icon: null | string;
            title: string;
        };
        PublicSectionBlock: {
            content: null | string;
            id: null | string;
            image_url: null | string;
            images: string[];
            items: components["schemas"]["PublicSectionHighlight"][];
            link_text: null | string;
            link_url: null | string;
            title: null | string;
            type: string;
        };
        PublicSectionBranding: {
            academy_socials: components["schemas"]["PublicSectionSocial"][];
            city: null | string;
            country: null | string;
            first_team_socials: components["schemas"]["PublicSectionSocial"][];
            greek_name: null | string;
            history: null | string;
            home_venue_id: null | string;
            id: string;
            legacy_socials: components["schemas"]["PublicSectionSocial"][];
            logo_url: null | string;
            name: string;
            organization_id: string;
            stadium: null | string;
            /** Format: int32 */
            stadium_capacity: null | number | string;
            stadium_dimensions: null | string;
            stadium_image_url: null | string;
            stadium_surface: null | string;
        };
        PublicSectionEvent: {
            /** Format: int32 */
            capacity: null | number | string;
            cover_image_url: null | string;
            description: null | string;
            /** Format: date-time */
            end_at: null | string;
            event_type: string;
            id: string;
            is_featured: boolean;
            location: null | string;
            organization_id: string;
            /** Format: double */
            price_eur: null | number | string;
            registration_url: null | string;
            /** Format: date-time */
            start_at: string;
            status: string;
            title: string;
        };
        PublicSectionExperience: {
            club: null | string;
            role: null | string;
            years: null | string;
        };
        PublicSectionFixture: {
            academy_group_id: null | string;
            /** Format: int32 */
            away_score: null | number | string;
            away_team: string;
            away_team_logo: null | string;
            competition: null | string;
            /** Format: int32 */
            home_score: null | number | string;
            home_team: string;
            home_team_logo: null | string;
            id: string;
            /** Format: date */
            match_date: string;
            match_time: null | string;
            organization_id: string;
            season: null | string;
            status: string;
            team_id: null | string;
            team_type: string;
            venue: null | string;
        };
        PublicSectionFixtureView: {
            academy_group_id?: null | string;
            /** Format: int32 */
            away_score?: null | number | string;
            away_team?: string;
            away_team_logo?: null | string;
            competition?: null | string;
            /** Format: int32 */
            home_score?: null | number | string;
            home_team?: string;
            home_team_logo?: null | string;
            id?: string;
            /** Format: date-time */
            kickoff_at: null | string;
            /** Format: date */
            match_date?: string;
            match_time?: null | string;
            organization_id?: string;
            season?: null | string;
            status?: string;
            team_id?: null | string;
            team_type?: string;
            venue?: null | string;
        };
        PublicSectionGallery: {
            academy_group_id: null | string;
            category: string;
            /** Format: date-time */
            created_at: null | string;
            description: null | string;
            id: string;
            image_url: string;
            is_featured: boolean;
            organization_id: string;
            player_id: null | string;
            team_id: null | string;
            title: string;
        };
        PublicSectionGroup: {
            coach_id: null | string;
            coach_image_url: null | string;
            coach_name: null | string;
            coach_role: null | string;
            id: string;
            organization_id: string;
        };
        PublicSectionHighlight: {
            label: null | string;
            value: null | string;
        };
        PublicSectionModule: {
            id: string;
            organization_id: string;
            /** Format: int32 */
            published_count: number | string;
            /** Format: int32 */
            quarantined_count: number | string;
            status: string;
        };
        PublicSectionPartner: {
            benefits: components["schemas"]["PublicSectionBenefit"][];
            bio: null | string;
            bio_bullets: string[];
            cta_button1_label: null | string;
            cta_button1_url: null | string;
            cta_button2_label: null | string;
            cta_button2_url: null | string;
            cta_text: null | string;
            cta_title: null | string;
            /** Format: int32 */
            display_order: number | string;
            executive_name: null | string;
            executive_role: null | string;
            headshot_url: null | string;
            hero_image_url: null | string;
            id: string;
            name: string;
            organization_id: string;
            partner_type: string;
            slug: string;
            socials: components["schemas"]["PublicSectionSocial"][];
            staff_members: components["schemas"]["PublicSectionPartnerStaff"][];
            tagline: null | string;
            website_url: null | string;
        };
        PublicSectionPartnerStaff: {
            bio: null | string;
            name: string;
            photo_url: null | string;
            role: null | string;
        };
        PublicSectionPlayer: {
            academy_group_ids: string[];
            academy_group_name: null | string;
            /** Format: int32 */
            age: null | number | string;
            bio: null | string;
            height: null | string;
            id: string;
            image_url: null | string;
            name: string;
            nationality: null | string;
            /** Format: int32 */
            number: number | string;
            organization_id: string;
            position: null | string;
            preferred_foot: null | string;
            previous_clubs: components["schemas"]["PublicSectionPreviousClub"][];
            statistics: null | components["schemas"]["PublicSectionStatistics"];
            team_id: null | string;
            team_type: string;
            weight: null | string;
        };
        PublicSectionPotm: {
            id: string;
            month_key: string;
            organization_id: string;
            results: components["schemas"]["PublicSectionPotmCandidate"][];
            /** Format: int32 */
            total_votes: number | string;
        };
        PublicSectionPotmCandidate: {
            image_url: null | string;
            name: string;
            /** Format: int32 */
            number: number | string;
            player_id: string;
            position: null | string;
            /** Format: int32 */
            votes: number | string;
        };
        PublicSectionPreviousClub: {
            club_name: string;
        };
        PublicSectionProduct: {
            category: string;
            delivery_options: string[];
            description: null | string;
            id: string;
            image_url: null | string;
            in_stock: boolean;
            name: string;
            organization_id: string;
            /** Format: double */
            price: number | string;
            product_type: string;
            sizes: string[];
        };
        PublicSectionReview: {
            author_name: string;
            author_role: null | string;
            body: string;
            id: string;
            organization_id: string;
            /** Format: date-time */
            published_at: null | string;
            /** Format: int32 */
            rating: number | string;
            response: null | string;
            title: null | string;
        };
        PublicSectionSeason: {
            competitions: string[];
            end_date: null | string;
            id: string;
            is_archived: boolean;
            is_current: boolean;
            name: string;
            organization_id: string;
            start_date: null | string;
        };
        PublicSectionSocial: {
            platform: string;
            url: string;
        };
        PublicSectionSponsor: {
            banner_url: null | string;
            content_blocks: components["schemas"]["PublicSectionBlock"][];
            description: null | string;
            /** Format: int32 */
            display_order: number | string;
            facebook: null | string;
            id: string;
            instagram: null | string;
            level: string;
            linkedin: null | string;
            logo_url: null | string;
            name: string;
            organization_id: string;
            sponsor_type: string;
            twitter: null | string;
            website: null | string;
            youtube: null | string;
        };
        PublicSectionsResult: {
            academy_groups: components["schemas"]["PublicSectionGroup"][];
            academy_stats: null | components["schemas"]["PublicSectionAcademyStats"];
            archived_seasons: components["schemas"]["PublicSectionArchive"][];
            branding: null | components["schemas"]["PublicSectionBranding"];
            events: components["schemas"]["PublicSectionEvent"][];
            fixtures: components["schemas"]["PublicSectionFixtureView"][];
            gallery: components["schemas"]["PublicSectionGallery"][];
            /** Format: date-time */
            generated_at: string;
            modules: components["schemas"]["PublicSectionModule"][];
            organization_id: string;
            partners: components["schemas"]["PublicSectionPartner"][];
            players: components["schemas"]["PublicSectionPlayer"][];
            potm: components["schemas"]["PublicSectionPotm"];
            products: components["schemas"]["PublicSectionProduct"][];
            reviews: components["schemas"]["PublicSectionReview"][];
            seasons: components["schemas"]["PublicSectionSeason"][];
            /** Format: date-time */
            snapshot_at: string;
            sponsors: components["schemas"]["PublicSectionSponsor"][];
            staff: components["schemas"]["PublicSectionStaff"][];
            standings: components["schemas"]["PublicSectionStanding"][];
            tickets: components["schemas"]["PublicSectionTicket"][];
            time_zone: string;
            trial_settings: null | components["schemas"]["PublicSectionTrials"];
            venues: components["schemas"]["PublicSectionVenue"][];
        };
        PublicSectionStaff: {
            academy_group_ids: string[];
            badges: string[];
            bio: null | string;
            coaching_philosophy: null | string;
            favorite_quote: null | string;
            id: string;
            image_url: null | string;
            joined_date: null | string;
            name: string;
            nationality: null | string;
            organization_id: string;
            previous_experience: components["schemas"]["PublicSectionExperience"][];
            qualifications: string[];
            role: string;
            team_ids: string[];
            team_type: string;
        };
        PublicSectionStanding: {
            competition: string;
            /** Format: int32 */
            drawn: number | string;
            form: null | string;
            /** Format: int32 */
            goal_difference: number | string;
            /** Format: int32 */
            goals_against: number | string;
            /** Format: int32 */
            goals_for: number | string;
            id: string;
            /** Format: int32 */
            lost: number | string;
            organization_id: string;
            /** Format: int32 */
            played: number | string;
            /** Format: int32 */
            points: number | string;
            season: null | string;
            team_logo: null | string;
            team_name: string;
            /** Format: int32 */
            won: number | string;
        };
        PublicSectionStatistics: {
            /** Format: int32 */
            appearances: null | number | string;
            /** Format: int32 */
            assists: null | number | string;
            /** Format: int32 */
            clean_sheets: null | number | string;
            /** Format: int32 */
            goals: null | number | string;
            /** Format: int32 */
            minutes_played: null | number | string;
            /** Format: int32 */
            red_cards: null | number | string;
            /** Format: int32 */
            yellow_cards: null | number | string;
        };
        PublicSectionTicket: {
            available: boolean;
            description: null | string;
            fixture_id: null | string;
            id: string;
            /** Format: int32 */
            max_quantity: number | string;
            name: string;
            organization_id: string;
            /** Format: double */
            price: number | string;
            ticket_type: string;
        };
        PublicSectionTrials: {
            button_text: null | string;
            closed_message: null | string;
            headline: null | string;
            id: string;
            open: boolean;
            organization_id: string;
            subtitle: null | string;
        };
        PublicSectionVenue: {
            address: null | string;
            /** Format: int32 */
            capacity: null | number | string;
            city: null | string;
            country: null | string;
            id: string;
            image_url: null | string;
            is_home_ground: boolean;
            /** Format: double */
            latitude: null | number | string;
            /** Format: double */
            longitude: null | number | string;
            map_url: null | string;
            name: string;
            organization_id: string;
            surface: null | string;
        };
        PublicSiteResult: {
            academy_groups: components["schemas"]["PublicAcademyGroup"][];
            club: null | components["schemas"]["PublicClub"];
            fixture_clock?: null | components["schemas"]["PublicFixtureInstant"][];
            fixtures: components["schemas"]["PublicFixture"][];
            /** Format: date-time */
            generated_at?: null | string;
            news: components["schemas"]["PublicArticle"][];
            organization_id: string;
            teams: components["schemas"]["PublicTeam"][];
            time_zone?: null | string;
        };
        PublicTeam: {
            banner_url: null | string;
            description: null | string;
            id: string;
            level: null | string;
            name: string;
            organization_id: string;
        };
        RecipientPendingInvitationDto: {
            /** Format: date-time */
            created_at: string;
            /** Format: date-time */
            expires_at: string;
            /** Format: uuid */
            id: string;
            organization_id: string;
            status: string;
        };
        RecipientPendingInvitationsResult: {
            items: components["schemas"]["RecipientPendingInvitationDto"][];
        };
        RoleSwitchBody: {
            role: null | string;
        };
        WebAdminError: {
            code: string;
            detail: string;
        };
        WebAdminIdentityDto: {
            /** Format: uuid */
            id: string;
            name: string;
            organizations: components["schemas"]["WebAdminOrganizationDto"][];
            permissions: string[];
            role: string;
            username: string;
        };
        WebAdminLoginCommand: {
            password: null | string;
            /** Format: int32 */
            remember_days: null | number | string;
            username: null | string;
        };
        WebAdminOrganizationDto: {
            id: string;
            name: string;
        };
        WebAdminSessionDto: {
            /** Format: date-time */
            expires_at: string;
            user: components["schemas"]["WebAdminIdentityDto"];
        };
    };
    responses: never;
    parameters: never;
    requestBodies: never;
    headers: never;
    pathItems: never;
}
export type $defs = Record<string, never>;
export interface operations {
    LoginWebAdmin: {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        requestBody: {
            content: {
                "application/json": components["schemas"]["WebAdminLoginCommand"];
            };
        };
        responses: {
            /** @description OK */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["WebAdminSessionDto"];
                };
            };
            /** @description Bad Request */
            400: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["WebAdminError"];
                };
            };
            /** @description Unauthorized */
            401: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["WebAdminError"];
                };
            };
            /** @description Forbidden */
            403: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["WebAdminError"];
                };
            };
            /** @description Payload Too Large */
            413: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["WebAdminError"];
                };
            };
            /** @description Unsupported Media Type */
            415: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["WebAdminError"];
                };
            };
            /** @description Unprocessable Entity */
            422: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["WebAdminError"];
                };
            };
            /** @description Too Many Requests */
            429: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["WebAdminError"];
                };
            };
            /** @description Service Unavailable */
            503: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["WebAdminError"];
                };
            };
        };
    };
    LogoutWebAdmin: {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        requestBody?: never;
        responses: {
            /** @description No Content */
            204: {
                headers: {
                    [name: string]: unknown;
                };
                content?: never;
            };
            /** @description Bad Request */
            400: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["WebAdminError"];
                };
            };
            /** @description Unauthorized */
            401: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["WebAdminError"];
                };
            };
            /** @description Forbidden */
            403: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["WebAdminError"];
                };
            };
            /** @description Payload Too Large */
            413: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["WebAdminError"];
                };
            };
            /** @description Unsupported Media Type */
            415: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["WebAdminError"];
                };
            };
            /** @description Unprocessable Entity */
            422: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["WebAdminError"];
                };
            };
            /** @description Too Many Requests */
            429: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["WebAdminError"];
                };
            };
            /** @description Service Unavailable */
            503: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["WebAdminError"];
                };
            };
        };
    };
    GetWebAdminSession: {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        requestBody?: never;
        responses: {
            /** @description OK */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["WebAdminSessionDto"];
                };
            };
            /** @description Bad Request */
            400: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["WebAdminError"];
                };
            };
            /** @description Unauthorized */
            401: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["WebAdminError"];
                };
            };
            /** @description Forbidden */
            403: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["WebAdminError"];
                };
            };
            /** @description Payload Too Large */
            413: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["WebAdminError"];
                };
            };
            /** @description Unsupported Media Type */
            415: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["WebAdminError"];
                };
            };
            /** @description Unprocessable Entity */
            422: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["WebAdminError"];
                };
            };
            /** @description Too Many Requests */
            429: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["WebAdminError"];
                };
            };
            /** @description Service Unavailable */
            503: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["WebAdminError"];
                };
            };
        };
    };
    GetAdminClubProfile: {
        parameters: {
            query: {
                /** @description One current authorized owner; no implicit or global organization. */
                organization_id: "lefteria_fc" | "lefteria_fc_academy";
            };
            header?: never;
            path?: never;
            cookie?: never;
        };
        requestBody?: never;
        responses: {
            /** @description OK */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["AdminClubProfileDto"];
                };
            };
            /** @description Bad Request */
            400: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["WebAdminError"];
                };
            };
            /** @description Unauthorized */
            401: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["WebAdminError"];
                };
            };
            /** @description Forbidden */
            403: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["WebAdminError"];
                };
            };
            /** @description Not Found */
            404: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["WebAdminError"];
                };
            };
            /** @description Conflict */
            409: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["WebAdminError"];
                };
            };
            /** @description Payload Too Large */
            413: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["WebAdminError"];
                };
            };
            /** @description Unsupported Media Type */
            415: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["WebAdminError"];
                };
            };
            /** @description Unprocessable Entity */
            422: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["WebAdminError"];
                };
            };
            /** @description Service Unavailable */
            503: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["WebAdminError"];
                };
            };
        };
    };
    UpdateAdminClubProfile: {
        parameters: {
            query: {
                /** @description One current authorized owner; no implicit or global organization. */
                organization_id: "lefteria_fc" | "lefteria_fc_academy";
            };
            header?: never;
            path?: never;
            cookie?: never;
        };
        requestBody: {
            content: {
                "application/json": components["schemas"]["AdminClubUpdateCommand"];
            };
        };
        responses: {
            /** @description OK */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["AdminClubProfileDto"];
                };
            };
            /** @description Bad Request */
            400: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["WebAdminError"];
                };
            };
            /** @description Unauthorized */
            401: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["WebAdminError"];
                };
            };
            /** @description Forbidden */
            403: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["WebAdminError"];
                };
            };
            /** @description Not Found */
            404: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["WebAdminError"];
                };
            };
            /** @description Conflict */
            409: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["WebAdminError"];
                };
            };
            /** @description Payload Too Large */
            413: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["WebAdminError"];
                };
            };
            /** @description Unsupported Media Type */
            415: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["WebAdminError"];
                };
            };
            /** @description Unprocessable Entity */
            422: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["WebAdminError"];
                };
            };
            /** @description Service Unavailable */
            503: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["WebAdminError"];
                };
            };
        };
    };
    GetLiveness: {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        requestBody?: never;
        responses: {
            /** @description OK */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["HealthResponse"];
                };
            };
        };
    };
    GetReadiness: {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        requestBody?: never;
        responses: {
            /** @description OK */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["HealthResponse"];
                };
            };
            /** @description Service Unavailable */
            503: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["HealthResponse"];
                };
            };
        };
    };
    ExchangeLegacyMobileSession: {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        requestBody?: never;
        responses: {
            /** @description OK */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["MobileLoginResult"];
                };
            };
            /** @description Bad Request */
            400: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["MobileAuthError"];
                };
            };
            /** @description Unauthorized */
            401: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["MobileAuthError"];
                };
            };
            /** @description Forbidden */
            403: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["MobileAuthError"];
                };
            };
            /** @description Not Found */
            404: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["MobileAuthError"];
                };
            };
            /** @description Unprocessable Entity */
            422: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["MobileAuthError"];
                };
            };
            /** @description Too Many Requests */
            429: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["MobileAuthError"];
                };
            };
            /** @description Service Unavailable */
            503: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["MobileAuthError"];
                };
            };
        };
    };
    GetMobileIdentity: {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        requestBody?: never;
        responses: {
            /** @description OK */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["MobileUserDto"];
                };
            };
            /** @description Bad Request */
            400: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["MobileAuthError"];
                };
            };
            /** @description Unauthorized */
            401: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["MobileAuthError"];
                };
            };
            /** @description Forbidden */
            403: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["MobileAuthError"];
                };
            };
            /** @description Not Found */
            404: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["MobileAuthError"];
                };
            };
            /** @description Unprocessable Entity */
            422: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["MobileAuthError"];
                };
            };
            /** @description Too Many Requests */
            429: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["MobileAuthError"];
                };
            };
            /** @description Service Unavailable */
            503: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["MobileAuthError"];
                };
            };
        };
    };
    UpdateMobileProfilePreference: {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        requestBody: {
            content: {
                "application/json": components["schemas"]["MobileProfilePreferenceBody"];
            };
        };
        responses: {
            /** @description OK */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["MobileProfilePreferenceResult"];
                };
            };
            /** @description Bad Request */
            400: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["MobileAuthError"];
                };
            };
            /** @description Unauthorized */
            401: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["MobileAuthError"];
                };
            };
            /** @description Forbidden */
            403: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["MobileAuthError"];
                };
            };
            /** @description Not Found */
            404: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["MobileAuthError"];
                };
            };
            /** @description Unprocessable Entity */
            422: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["MobileAuthError"];
                };
            };
            /** @description Too Many Requests */
            429: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["MobileAuthError"];
                };
            };
            /** @description Service Unavailable */
            503: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["MobileAuthError"];
                };
            };
        };
    };
    RegisterMobileFan: {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        requestBody: {
            content: {
                "application/json": components["schemas"]["FanRegistrationBody"];
            };
        };
        responses: {
            /** @description OK */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["FanRegistrationResult"];
                };
            };
            /** @description Bad Request */
            400: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["MobileAuthError"];
                };
            };
            /** @description Unauthorized */
            401: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["MobileAuthError"];
                };
            };
            /** @description Forbidden */
            403: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["MobileAuthError"];
                };
            };
            /** @description Not Found */
            404: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["MobileAuthError"];
                };
            };
            /** @description Unprocessable Entity */
            422: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["MobileAuthError"];
                };
            };
            /** @description Too Many Requests */
            429: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["MobileAuthError"];
                };
            };
            /** @description Service Unavailable */
            503: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["MobileAuthError"];
                };
            };
        };
    };
    RequestMobileOtp: {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        requestBody: {
            content: {
                "application/json": components["schemas"]["OtpRequestBody"];
            };
        };
        responses: {
            /** @description OK */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["OtpRequestResult"];
                };
            };
            /** @description Bad Request */
            400: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["MobileAuthError"];
                };
            };
            /** @description Unauthorized */
            401: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["MobileAuthError"];
                };
            };
            /** @description Forbidden */
            403: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["MobileAuthError"];
                };
            };
            /** @description Not Found */
            404: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["MobileAuthError"];
                };
            };
            /** @description Unprocessable Entity */
            422: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["MobileAuthError"];
                };
            };
            /** @description Too Many Requests */
            429: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["MobileAuthError"];
                };
            };
            /** @description Service Unavailable */
            503: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["MobileAuthError"];
                };
            };
        };
    };
    SwitchMobileRole: {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        requestBody: {
            content: {
                "application/json": components["schemas"]["RoleSwitchBody"];
            };
        };
        responses: {
            /** @description OK */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["MobileRoleSwitchResult"];
                };
            };
            /** @description Bad Request */
            400: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["MobileAuthError"];
                };
            };
            /** @description Unauthorized */
            401: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["MobileAuthError"];
                };
            };
            /** @description Forbidden */
            403: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["MobileAuthError"];
                };
            };
            /** @description Not Found */
            404: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["MobileAuthError"];
                };
            };
            /** @description Unprocessable Entity */
            422: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["MobileAuthError"];
                };
            };
            /** @description Too Many Requests */
            429: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["MobileAuthError"];
                };
            };
            /** @description Service Unavailable */
            503: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["MobileAuthError"];
                };
            };
        };
    };
    VerifyMobileOtp: {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        requestBody: {
            content: {
                "application/json": components["schemas"]["OtpVerificationBody"];
            };
        };
        responses: {
            /** @description OK */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["MobileVerificationResult"];
                };
            };
            /** @description Bad Request */
            400: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["MobileAuthError"];
                };
            };
            /** @description Unauthorized */
            401: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["MobileAuthError"];
                };
            };
            /** @description Forbidden */
            403: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["MobileAuthError"];
                };
            };
            /** @description Not Found */
            404: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["MobileAuthError"];
                };
            };
            /** @description Unprocessable Entity */
            422: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["MobileAuthError"];
                };
            };
            /** @description Too Many Requests */
            429: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["MobileAuthError"];
                };
            };
            /** @description Service Unavailable */
            503: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["MobileAuthError"];
                };
            };
        };
    };
    SubmitMobileAvailability: {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        requestBody: {
            content: {
                "application/json": components["schemas"]["MobileAvailabilityBody"];
            };
        };
        responses: {
            /** @description OK */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["MobileAvailabilityResult"];
                };
            };
            /** @description Bad Request */
            400: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["MobileAuthError"];
                };
            };
            /** @description Unauthorized */
            401: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["MobileAuthError"];
                };
            };
            /** @description Forbidden */
            403: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["MobileAuthError"];
                };
            };
            /** @description Not Found */
            404: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["MobileAuthError"];
                };
            };
            /** @description Payload Too Large */
            413: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["MobileAuthError"];
                };
            };
            /** @description Unprocessable Entity */
            422: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["MobileAuthError"];
                };
            };
            /** @description Too Many Requests */
            429: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["MobileAuthError"];
                };
            };
            /** @description Service Unavailable */
            503: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["MobileAuthError"];
                };
            };
        };
    };
    GetMobileCalendar: {
        parameters: {
            query?: {
                month?: string;
                offset?: string;
                player_id?: string;
            };
            header?: never;
            path?: never;
            cookie?: never;
        };
        requestBody?: never;
        responses: {
            /** @description OK */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["MobileCalendarResult"];
                };
            };
            /** @description Bad Request */
            400: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["MobileAuthError"];
                };
            };
            /** @description Unauthorized */
            401: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["MobileAuthError"];
                };
            };
            /** @description Forbidden */
            403: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["MobileAuthError"];
                };
            };
            /** @description Not Found */
            404: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["MobileAuthError"];
                };
            };
            /** @description Unprocessable Entity */
            422: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["MobileAuthError"];
                };
            };
            /** @description Too Many Requests */
            429: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["MobileAuthError"];
                };
            };
            /** @description Service Unavailable */
            503: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["MobileAuthError"];
                };
            };
        };
    };
    GetMobileEventResponses: {
        parameters: {
            query?: {
                offset?: string;
            };
            header?: never;
            path: {
                event_id: string;
                event_type: string;
            };
            cookie?: never;
        };
        requestBody?: never;
        responses: {
            /** @description OK */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["MobileCalendarResponseResult"];
                };
            };
            /** @description Bad Request */
            400: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["MobileAuthError"];
                };
            };
            /** @description Unauthorized */
            401: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["MobileAuthError"];
                };
            };
            /** @description Forbidden */
            403: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["MobileAuthError"];
                };
            };
            /** @description Not Found */
            404: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["MobileAuthError"];
                };
            };
            /** @description Unprocessable Entity */
            422: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["MobileAuthError"];
                };
            };
            /** @description Too Many Requests */
            429: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["MobileAuthError"];
                };
            };
            /** @description Service Unavailable */
            503: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["MobileAuthError"];
                };
            };
        };
    };
    GetParentFamily: {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        requestBody?: never;
        responses: {
            /** @description OK */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ParentFamilyResult"];
                };
            };
            /** @description Bad Request */
            400: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["MobileAuthError"];
                };
            };
            /** @description Unauthorized */
            401: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["MobileAuthError"];
                };
            };
            /** @description Forbidden */
            403: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["MobileAuthError"];
                };
            };
            /** @description Not Found */
            404: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["MobileAuthError"];
                };
            };
            /** @description Unprocessable Entity */
            422: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["MobileAuthError"];
                };
            };
            /** @description Too Many Requests */
            429: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["MobileAuthError"];
                };
            };
            /** @description Service Unavailable */
            503: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["MobileAuthError"];
                };
            };
        };
    };
    GetParentActiveFamilyDelegations: {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        requestBody?: never;
        responses: {
            /** @description OK */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ParentActiveDelegationsResult"];
                };
            };
            /** @description Bad Request */
            400: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["MobileAuthError"];
                };
            };
            /** @description Unauthorized */
            401: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["MobileAuthError"];
                };
            };
            /** @description Forbidden */
            403: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["MobileAuthError"];
                };
            };
            /** @description Not Found */
            404: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["MobileAuthError"];
                };
            };
            /** @description Unprocessable Entity */
            422: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["MobileAuthError"];
                };
            };
            /** @description Too Many Requests */
            429: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["MobileAuthError"];
                };
            };
            /** @description Service Unavailable */
            503: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["MobileAuthError"];
                };
            };
        };
    };
    GetRecipientFamilyDelegationAvailability: {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        requestBody?: never;
        responses: {
            /** @description OK */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["FamilyDelegationAvailabilityDto"];
                };
            };
            /** @description Bad Request */
            400: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["MobileAuthError"];
                };
            };
            /** @description Unauthorized */
            401: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["MobileAuthError"];
                };
            };
            /** @description Forbidden */
            403: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["MobileAuthError"];
                };
            };
            /** @description Not Found */
            404: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["MobileAuthError"];
                };
            };
            /** @description Unprocessable Entity */
            422: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["MobileAuthError"];
                };
            };
            /** @description Too Many Requests */
            429: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["MobileAuthError"];
                };
            };
            /** @description Service Unavailable */
            503: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["MobileAuthError"];
                };
            };
        };
    };
    SubmitDelegatedFamilyAvailability: {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        requestBody: {
            content: {
                "application/json": components["schemas"]["MobileAvailabilityBody"];
            };
        };
        responses: {
            /** @description OK */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["MobileAvailabilityResult"];
                };
            };
            /** @description Bad Request */
            400: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["MobileAuthError"];
                };
            };
            /** @description Unauthorized */
            401: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["MobileAuthError"];
                };
            };
            /** @description Forbidden */
            403: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["MobileAuthError"];
                };
            };
            /** @description Not Found */
            404: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["MobileAuthError"];
                };
            };
            /** @description Payload Too Large */
            413: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["MobileAuthError"];
                };
            };
            /** @description Unprocessable Entity */
            422: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["MobileAuthError"];
                };
            };
            /** @description Too Many Requests */
            429: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["MobileAuthError"];
                };
            };
            /** @description Service Unavailable */
            503: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["MobileAuthError"];
                };
            };
        };
    };
    GetDelegatedFamilyCalendar: {
        parameters: {
            query?: {
                month?: string;
                offset?: string;
            };
            header?: never;
            path?: never;
            cookie?: never;
        };
        requestBody?: never;
        responses: {
            /** @description OK */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["MobileCalendarResult"];
                };
            };
            /** @description Bad Request */
            400: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["MobileAuthError"];
                };
            };
            /** @description Unauthorized */
            401: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["MobileAuthError"];
                };
            };
            /** @description Forbidden */
            403: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["MobileAuthError"];
                };
            };
            /** @description Not Found */
            404: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["MobileAuthError"];
                };
            };
            /** @description Unprocessable Entity */
            422: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["MobileAuthError"];
                };
            };
            /** @description Too Many Requests */
            429: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["MobileAuthError"];
                };
            };
            /** @description Service Unavailable */
            503: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["MobileAuthError"];
                };
            };
        };
    };
    IssuePendingFamilyInvitation: {
        parameters: {
            query?: never;
            header?: never;
            path: {
                playerId: string;
            };
            cookie?: never;
        };
        requestBody: {
            content: {
                "application/json": components["schemas"]["IssueFamilyInvitationBody"];
            };
        };
        responses: {
            /** @description OK */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["PendingFamilyInvitationDto"];
                };
            };
            /** @description Bad Request */
            400: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["MobileAuthError"];
                };
            };
            /** @description Unauthorized */
            401: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["MobileAuthError"];
                };
            };
            /** @description Forbidden */
            403: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["MobileAuthError"];
                };
            };
            /** @description Not Found */
            404: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["MobileAuthError"];
                };
            };
            /** @description Conflict */
            409: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["MobileAuthError"];
                };
            };
            /** @description Unprocessable Entity */
            422: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["MobileAuthError"];
                };
            };
            /** @description Too Many Requests */
            429: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["MobileAuthError"];
                };
            };
            /** @description Service Unavailable */
            503: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["MobileAuthError"];
                };
            };
        };
    };
    RevokeCurrentFamilyPlayerDelegation: {
        parameters: {
            query?: never;
            header?: never;
            path: {
                playerId: string;
            };
            cookie?: never;
        };
        requestBody?: never;
        responses: {
            /** @description OK */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["FamilyOperationResult"];
                };
            };
            /** @description Bad Request */
            400: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["MobileAuthError"];
                };
            };
            /** @description Unauthorized */
            401: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["MobileAuthError"];
                };
            };
            /** @description Forbidden */
            403: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["MobileAuthError"];
                };
            };
            /** @description Not Found */
            404: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["MobileAuthError"];
                };
            };
            /** @description Conflict */
            409: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["MobileAuthError"];
                };
            };
            /** @description Unprocessable Entity */
            422: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["MobileAuthError"];
                };
            };
            /** @description Too Many Requests */
            429: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["MobileAuthError"];
                };
            };
            /** @description Service Unavailable */
            503: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["MobileAuthError"];
                };
            };
        };
    };
    GetRecipientPendingFamilyInvitations: {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        requestBody?: never;
        responses: {
            /** @description OK */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["RecipientPendingInvitationsResult"];
                };
            };
            /** @description Bad Request */
            400: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["MobileAuthError"];
                };
            };
            /** @description Unauthorized */
            401: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["MobileAuthError"];
                };
            };
            /** @description Forbidden */
            403: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["MobileAuthError"];
                };
            };
            /** @description Not Found */
            404: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["MobileAuthError"];
                };
            };
            /** @description Unprocessable Entity */
            422: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["MobileAuthError"];
                };
            };
            /** @description Too Many Requests */
            429: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["MobileAuthError"];
                };
            };
            /** @description Service Unavailable */
            503: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["MobileAuthError"];
                };
            };
        };
    };
    AcceptVerifiedRecipientFamilyInvitation: {
        parameters: {
            query?: never;
            header?: never;
            path: {
                invitationId: string;
            };
            cookie?: never;
        };
        requestBody?: never;
        responses: {
            /** @description OK */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["FamilyOperationResult"];
                };
            };
            /** @description Bad Request */
            400: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["MobileAuthError"];
                };
            };
            /** @description Unauthorized */
            401: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["MobileAuthError"];
                };
            };
            /** @description Forbidden */
            403: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["MobileAuthError"];
                };
            };
            /** @description Not Found */
            404: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["MobileAuthError"];
                };
            };
            /** @description Conflict */
            409: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["MobileAuthError"];
                };
            };
            /** @description Unprocessable Entity */
            422: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["MobileAuthError"];
                };
            };
            /** @description Too Many Requests */
            429: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["MobileAuthError"];
                };
            };
            /** @description Service Unavailable */
            503: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["MobileAuthError"];
                };
            };
        };
    };
    CancelPendingFamilyInvitation: {
        parameters: {
            query?: never;
            header?: never;
            path: {
                invitationId: string;
            };
            cookie?: never;
        };
        requestBody?: never;
        responses: {
            /** @description OK */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["FamilyOperationResult"];
                };
            };
            /** @description Bad Request */
            400: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["MobileAuthError"];
                };
            };
            /** @description Unauthorized */
            401: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["MobileAuthError"];
                };
            };
            /** @description Forbidden */
            403: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["MobileAuthError"];
                };
            };
            /** @description Not Found */
            404: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["MobileAuthError"];
                };
            };
            /** @description Conflict */
            409: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["MobileAuthError"];
                };
            };
            /** @description Unprocessable Entity */
            422: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["MobileAuthError"];
                };
            };
            /** @description Too Many Requests */
            429: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["MobileAuthError"];
                };
            };
            /** @description Service Unavailable */
            503: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["MobileAuthError"];
                };
            };
        };
    };
    DeclineRecipientPendingFamilyInvitation: {
        parameters: {
            query?: never;
            header?: never;
            path: {
                invitationId: string;
            };
            cookie?: never;
        };
        requestBody?: never;
        responses: {
            /** @description OK */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["FamilyOperationResult"];
                };
            };
            /** @description Bad Request */
            400: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["MobileAuthError"];
                };
            };
            /** @description Unauthorized */
            401: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["MobileAuthError"];
                };
            };
            /** @description Forbidden */
            403: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["MobileAuthError"];
                };
            };
            /** @description Not Found */
            404: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["MobileAuthError"];
                };
            };
            /** @description Unprocessable Entity */
            422: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["MobileAuthError"];
                };
            };
            /** @description Too Many Requests */
            429: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["MobileAuthError"];
                };
            };
            /** @description Service Unavailable */
            503: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["MobileAuthError"];
                };
            };
        };
    };
    GetParentOutgoingInvitations: {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        requestBody?: never;
        responses: {
            /** @description OK */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ParentOutgoingInvitationsResult"];
                };
            };
            /** @description Bad Request */
            400: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["MobileAuthError"];
                };
            };
            /** @description Unauthorized */
            401: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["MobileAuthError"];
                };
            };
            /** @description Forbidden */
            403: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["MobileAuthError"];
                };
            };
            /** @description Not Found */
            404: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["MobileAuthError"];
                };
            };
            /** @description Unprocessable Entity */
            422: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["MobileAuthError"];
                };
            };
            /** @description Too Many Requests */
            429: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["MobileAuthError"];
                };
            };
            /** @description Service Unavailable */
            503: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["MobileAuthError"];
                };
            };
        };
    };
    GetMobileOrganization: {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        requestBody?: never;
        responses: {
            /** @description OK */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["MobileOrganizationContextDto"];
                };
            };
            /** @description Bad Request */
            400: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["MobileAuthError"];
                };
            };
            /** @description Unauthorized */
            401: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["MobileAuthError"];
                };
            };
            /** @description Forbidden */
            403: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["MobileAuthError"];
                };
            };
            /** @description Not Found */
            404: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["MobileAuthError"];
                };
            };
            /** @description Unprocessable Entity */
            422: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["MobileAuthError"];
                };
            };
            /** @description Too Many Requests */
            429: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["MobileAuthError"];
                };
            };
            /** @description Service Unavailable */
            503: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["MobileAuthError"];
                };
            };
        };
    };
    SelectMobileOrganization: {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        requestBody: {
            content: {
                "application/json": components["schemas"]["OrganizationSelectionBody"];
            };
        };
        responses: {
            /** @description OK */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["MobileOrganizationContextDto"];
                };
            };
            /** @description Bad Request */
            400: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["MobileAuthError"];
                };
            };
            /** @description Unauthorized */
            401: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["MobileAuthError"];
                };
            };
            /** @description Forbidden */
            403: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["MobileAuthError"];
                };
            };
            /** @description Not Found */
            404: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["MobileAuthError"];
                };
            };
            /** @description Unprocessable Entity */
            422: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["MobileAuthError"];
                };
            };
            /** @description Too Many Requests */
            429: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["MobileAuthError"];
                };
            };
            /** @description Service Unavailable */
            503: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["MobileAuthError"];
                };
            };
        };
    };
    GetMobileOwnAvailability: {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        requestBody?: never;
        responses: {
            /** @description OK */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["MobileAvailabilityRow"][];
                };
            };
            /** @description Bad Request */
            400: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["MobileAuthError"];
                };
            };
            /** @description Unauthorized */
            401: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["MobileAuthError"];
                };
            };
            /** @description Forbidden */
            403: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["MobileAuthError"];
                };
            };
            /** @description Not Found */
            404: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["MobileAuthError"];
                };
            };
            /** @description Unprocessable Entity */
            422: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["MobileAuthError"];
                };
            };
            /** @description Too Many Requests */
            429: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["MobileAuthError"];
                };
            };
            /** @description Service Unavailable */
            503: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["MobileAuthError"];
                };
            };
        };
    };
    GetMobilePlayersList: {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        requestBody?: never;
        responses: {
            /** @description OK */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["MobilePlayersListResult"];
                };
            };
            /** @description Bad Request */
            400: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["MobileAuthError"];
                };
            };
            /** @description Unauthorized */
            401: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["MobileAuthError"];
                };
            };
            /** @description Forbidden */
            403: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["MobileAuthError"];
                };
            };
            /** @description Not Found */
            404: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["MobileAuthError"];
                };
            };
            /** @description Unprocessable Entity */
            422: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["MobileAuthError"];
                };
            };
            /** @description Too Many Requests */
            429: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["MobileAuthError"];
                };
            };
            /** @description Service Unavailable */
            503: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["MobileAuthError"];
                };
            };
        };
    };
    UpdateMobileProfile: {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        requestBody: {
            content: {
                "application/json": components["schemas"]["MobileProfileUpdateBody"];
            };
        };
        responses: {
            /** @description OK */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["MobileUserDto"];
                };
            };
            /** @description Bad Request */
            400: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["MobileAuthError"];
                };
            };
            /** @description Unauthorized */
            401: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["MobileAuthError"];
                };
            };
            /** @description Forbidden */
            403: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["MobileAuthError"];
                };
            };
            /** @description Not Found */
            404: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["MobileAuthError"];
                };
            };
            /** @description Payload Too Large */
            413: {
                headers: {
                    [name: string]: unknown;
                };
                content?: never;
            };
            /** @description Unprocessable Entity */
            422: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["MobileAuthError"];
                };
            };
            /** @description Too Many Requests */
            429: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["MobileAuthError"];
                };
            };
            /** @description Service Unavailable */
            503: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["MobileAuthError"];
                };
            };
        };
    };
    UploadMobileAvatar: {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        requestBody: {
            content: {
                "multipart/form-data": components["schemas"]["MobileAvatarUploadBody"];
            };
        };
        responses: {
            /** @description OK */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["MobileAvatarResult"];
                };
            };
            /** @description Bad Request */
            400: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["MobileAuthError"];
                };
            };
            /** @description Unauthorized */
            401: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["MobileAuthError"];
                };
            };
            /** @description Forbidden */
            403: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["MobileAuthError"];
                };
            };
            /** @description Not Found */
            404: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["MobileAuthError"];
                };
            };
            /** @description Payload Too Large */
            413: {
                headers: {
                    [name: string]: unknown;
                };
                content?: never;
            };
            /** @description Unprocessable Entity */
            422: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["MobileAuthError"];
                };
            };
            /** @description Too Many Requests */
            429: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["MobileAuthError"];
                };
            };
            /** @description Service Unavailable */
            503: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["MobileAuthError"];
                };
            };
        };
    };
    GetPublicNews: {
        parameters: {
            query?: {
                category?: string;
                limit?: number | string;
                offset?: number | string;
                organization_id?: string;
            };
            header?: never;
            path?: never;
            cookie?: never;
        };
        requestBody?: never;
        responses: {
            /** @description OK */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["PublicNewsResult"];
                };
            };
            /** @description Bad Request */
            400: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["PublicContentError"];
                };
            };
            /** @description Service Unavailable */
            503: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["PublicContentError"];
                };
            };
        };
    };
    GetPublicNewsArticle: {
        parameters: {
            query?: {
                organization_id?: string;
            };
            header?: never;
            path: {
                id: string;
            };
            cookie?: never;
        };
        requestBody?: never;
        responses: {
            /** @description OK */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["PublicArticle"];
                };
            };
            /** @description Bad Request */
            400: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["PublicContentError"];
                };
            };
            /** @description Not Found */
            404: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["PublicContentError"];
                };
            };
            /** @description Service Unavailable */
            503: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["PublicContentError"];
                };
            };
        };
    };
    GetPublicSections: {
        parameters: {
            query?: {
                organization_id?: string;
            };
            header?: never;
            path?: never;
            cookie?: never;
        };
        requestBody?: never;
        responses: {
            /** @description OK */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["PublicSectionsResult"];
                };
            };
            /** @description Bad Request */
            400: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["PublicContentError"];
                };
            };
            /** @description Service Unavailable */
            503: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["PublicContentError"];
                };
            };
        };
    };
    GetPublicSite: {
        parameters: {
            query?: {
                organization_id?: string;
            };
            header?: never;
            path?: never;
            cookie?: never;
        };
        requestBody?: never;
        responses: {
            /** @description OK */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["PublicSiteResult"];
                };
            };
            /** @description Bad Request */
            400: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["PublicContentError"];
                };
            };
            /** @description Service Unavailable */
            503: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["PublicContentError"];
                };
            };
        };
    };
    GetMobileAvatar: {
        parameters: {
            query?: never;
            header?: never;
            path: {
                fileName: string;
            };
            cookie?: never;
        };
        requestBody?: never;
        responses: {
            /** @description OK */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content?: never;
            };
            /** @description Not Found */
            404: {
                headers: {
                    [name: string]: unknown;
                };
                content?: never;
            };
            /** @description Service Unavailable */
            503: {
                headers: {
                    [name: string]: unknown;
                };
                content?: never;
            };
        };
    };
    HeadMobileAvatar: {
        parameters: {
            query?: never;
            header?: never;
            path: {
                fileName: string;
            };
            cookie?: never;
        };
        requestBody?: never;
        responses: {
            /** @description OK */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content?: never;
            };
            /** @description Not Found */
            404: {
                headers: {
                    [name: string]: unknown;
                };
                content?: never;
            };
            /** @description Service Unavailable */
            503: {
                headers: {
                    [name: string]: unknown;
                };
                content?: never;
            };
        };
    };
}
