# Academy KOA health-card — parent instructions and message gate

**Status: DRAFT — no message sent; provider integration, consent and live staging acceptance not verified.**

## Trigger, order, and privacy
Only after an authorized Academy admin or location manager approves a registration **and** assigns the child to the permitted team, verify guardian identity and specific opt-in to Academy Viber notifications.

1. Reserve one **welcome-to-team** event in a durable idempotent outbox linked to the approved placement and verified guardian.
2. After welcome delivery has been accepted by the provider (without equating acceptance with device delivery), check the child's recorded KOA Health Card status.
3. If the card is recorded as **present**, do not queue KOA instructions. If **missing**, queue the KOA instruction message once, using its own idempotency key and current consent check.
4. If consent or current status cannot be verified, do not send. Never include identity-document scans, birth certificates, medical results or private attachments in outbound Viber text.
5. Revocation, transfer, duplicate webhook, rate limits and uncertain provider responses require safe non-resend handling and audit. No live Viber provider activation in this branch.

## Welcome message (Greek — candidate)

Καλώς ήρθατε στην οικογένεια της Λευτεριάς FC! Η εγγραφή του/της {ΟΝΟΜΑ_ΠΑΙΔΙΟΥ} εγκρίθηκε και η ένταξή του/της στην ομάδα {ΟΜΑΔΑ} επιβεβαιώθηκε. Ο υπεύθυνος τοποθεσίας θα σας ενημερώσει για τις επόμενες δραστηριότητες.

## KOA Health Card instructions (Greek — send only when missing)

Για τον/την {ΟΝΟΜΑ_ΠΑΙΔΙΟΥ} δεν έχει δηλωθεί εκδομένο Δελτίο Υγείας ΚΟΑ.

1. Κλείστε ραντεβού για την απαιτούμενη ιατρική εξέταση στο Μέλαθρον, σύμφωνα με τις εκάστοτε οδηγίες του φορέα.
2. Μετά την εξέταση, συγκεντρώστε τα απαιτούμενα αποτελέσματα, το έγγραφο ταυτότητας του παιδιού (ή πιστοποιητικό γέννησης) και **μία φωτογραφία τύπου διαβατηρίου**.
3. Στείλτε τον φάκελο στον ΚΟΑ μέσω ACS. Βάλτε **€5 μετρητά μέσα στον φάκελο** μαζί με τα απαιτούμενα δικαιολογητικά για την έκδοση Δελτίου Υγείας. **Η ACS χρεώνει ξεχωριστά** για τη μεταφορά — τα έξοδα αποστολής δεν περιλαμβάνονται στα €5.
4. Για την κανονική διαδικασία υπολογίστε **14 εργάσιμες ημέρες από την επίσημη παραλαβή του φακέλου στον ΚΟΑ**, όχι από την ημερομηνία παράδοσης στην ACS.
5. Πριν από την αποστολή, ελέγξτε τα ενημερωμένα έντυπα και τις οδηγίες ΚΟΑ: https://cso.org.cy/health-journal/ . Ο υπεύθυνος τοποθεσίας μπορεί να σας βοηθήσει με τη διαδικασία.

**Operational verification still needed:** exact Melathron appointment provider/channel, current ACS recipient address, eligible identity-document format (original vs copy), KOA updated 14-working-day service conditions and any changes to cash-in-envelope policy. Do not invent those fields or direct families to an unverified address.

## Charge boundaries
- **€5 KOA fee** goes in the KOA envelope; it is **not** an Academy registration fee, kit charge, monthly tuition payment or an additional invoice by Lefteria FC.
- **ACS delivery charges** are separate and cannot be assumed fixed.
- Academy pricing remains per-child registration €20, compulsory kit €140 + printing €10, family monthly €60 (one) / €100 (two) / +€50 each further child; September–June only, half first month after the 15th as a proposed season policy.
- No Viber send, fee posting, ACS shipment or money collection is implemented by this template.

**ONE TEAM ● ONE DREAM**
