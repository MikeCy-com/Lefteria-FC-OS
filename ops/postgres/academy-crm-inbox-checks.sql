\set ON_ERROR_STOP on
-- Disposable owner-scope CRM test. Synthetic phone/email, no real users or
-- messages, no Twilio, no enrollment. Everything rolls back.
BEGIN;
DO $academy_crm_checks$
DECLARE
  v_manager uuid:=gen_random_uuid();
  v_digest text:=repeat('c',64);
  v_lead uuid:=gen_random_uuid();
  v_result jsonb;
  v_event_count integer;
BEGIN
  INSERT INTO identity.admin_accounts(
    id,username,display_name,role,is_active,password_hash)
  VALUES(v_manager,'ci_crm_'||left(replace(v_manager::text,'-',''),16),
    'Disposable Academy manager','manager',true,
    'ci_test_hash_not_a_usable_password');
  INSERT INTO identity.admin_organization_grants(admin_account_id,organization_id)
    VALUES(v_manager,'lefteria_fc_academy');
  INSERT INTO identity.admin_sessions(
    id,token_digest,admin_account_id,credential_digest,created_at,expires_at)
    VALUES(gen_random_uuid(),v_digest,v_manager,repeat('a',64),
      clock_timestamp(),clock_timestamp()+interval '1 hour');
  INSERT INTO marketing.public_leads(
    id,organization_id,contact_name,phone,email,player_age,interested_in,
    message,source,partner_slug,consent_gdpr,consent_marketing,consent_recorded_at,status)
  VALUES(v_lead,'lefteria_fc_academy','Test Guardian','+35799123456',
    'crm@example.invalid',11,'Academy training','Synthetic enquiry only',
    'website',NULL,true,false,clock_timestamp(),'new');

  v_result:=marketing.read_admin_academy_leads(v_digest,'lefteria_fc_academy',0,25,'new');
  IF (v_result->>'total')::int<>1 OR jsonb_array_length(v_result->'leads')<>1
      OR v_result->'leads'->0->>'id'<>v_lead::text
      OR (v_result->'leads'->0->>'consent_marketing')::boolean THEN
    RAISE EXCEPTION 'Verified scoped Academy leads were not returned';
  END IF;

  BEGIN
    PERFORM marketing.read_admin_academy_leads(v_digest,'lefteria_fc',0,25,NULL);
    RAISE EXCEPTION 'Cross-organization CRM read succeeded';
  EXCEPTION WHEN invalid_parameter_value THEN NULL;
  END;
  BEGIN
    PERFORM marketing.read_admin_academy_leads(repeat('0',64),
      'lefteria_fc_academy',0,25,NULL);
    RAISE EXCEPTION 'Unauthorized CRM read succeeded';
  EXCEPTION WHEN insufficient_privilege THEN NULL;
  END;
  BEGIN
    PERFORM marketing.read_admin_academy_leads(v_digest,
      'lefteria_fc_academy',0,1000,NULL);
    RAISE EXCEPTION 'Unbounded CRM read succeeded';
  EXCEPTION WHEN invalid_parameter_value THEN NULL;
  END;

  v_result:=marketing.update_admin_academy_lead_status(
    v_digest,'lefteria_fc_academy',v_lead,'new','contacted');
  IF v_result->>'status'<>'contacted' OR
      (v_result->>'changed')::boolean IS DISTINCT FROM true THEN
    RAISE EXCEPTION 'CRM contact event not committed';
  END IF;
  v_result:=marketing.update_admin_academy_lead_status(
    v_digest,'lefteria_fc_academy',v_lead,'new','contacted');
  IF v_result->>'status'<>'contacted' OR
      (v_result->>'replay')::boolean IS DISTINCT FROM true THEN
    RAISE EXCEPTION 'CRM duplicate followup was not idempotent';
  END IF;
  BEGIN
    PERFORM marketing.update_admin_academy_lead_status(
      v_digest,'lefteria_fc_academy',v_lead,'new','registered');
    RAISE EXCEPTION 'CRM fabricated player registration';
  EXCEPTION WHEN invalid_parameter_value THEN NULL;
  END;
  v_result:=marketing.update_admin_academy_lead_status(
    v_digest,'lefteria_fc_academy',v_lead,'contacted','lost');
  IF v_result->>'status'<>'lost' THEN
    RAISE EXCEPTION 'CRM lost-contact event not saved';
  END IF;
  BEGIN
    PERFORM marketing.update_admin_academy_lead_status(
      v_digest,'lefteria_fc_academy',v_lead,'contacted','registered');
    RAISE EXCEPTION 'Terminal CRM lead reopened without enrollment';
  EXCEPTION WHEN invalid_parameter_value THEN NULL;
  END;
  SELECT count(*) INTO v_event_count FROM marketing.lead_review_events
    WHERE lead_id=v_lead;
  IF v_event_count<>2 OR
     (SELECT status FROM marketing.public_leads WHERE id=v_lead)<>'lost' THEN
    RAISE EXCEPTION 'CRM event log duplicated or database status wrong';
  END IF;
  IF NOT EXISTS(SELECT 1 FROM audit.security_events
    WHERE actor_identity_id=v_manager AND
    action='admin.crm.lead.status_changed' AND reason_code='lost') THEN
    RAISE EXCEPTION 'Global admin security audit missing';
  END IF;
END $academy_crm_checks$;
ROLLBACK;
