# Parent Academy education hub — editorial starter slice

Original repo MikeCy-com/Lefteria-FC is reference-only. This implementation targets Lefteria-FC-OS.

## Implemented
- Parent-only Education tab in the authenticated mobile app, accessible from Parent → More and the Parent Home quick-access section.
- Eight original-language educational mini-articles: parent and player for ages 6–9, 10–12, 13–15 and 16–18.
- Covers parent encouragement, developmental psychology, emotional resilience, communication, and school–sport balance.
- Age-stage and audience controls allow users to browse content without reading or storing anyone's age, health records, or personal identifiers.
- Readable article cards with expandable suggested approaches and discussion questions, simple responsive visual illustrations.
- Content clearly marked as **OS editorial starter material**, not Academy-approved or Academy-published content. Future Academy article uploads, real pictures, publishing review, copyright clearance and moderation remain separate work.
- No public API, child data projection, payment, SMS, account update, data migration or notification.
- The authenticated route only shows the hub for current Parent role; navigation normalization rejects education as a tab for fan, player, coach and management.

## Safety and next steps
Children and young athletes should not be compared to peers or held to rigid performance/appearance ideals. Content emphasizes learning, age appropriateness and safe trusted-adult help. It is educational and does not replace professional support.

The Academy publishing pipeline, images, role-based editorial approval, versions and author attribution need a separately reviewed implementation. Until then the OS does not claim that material was sent by Lefteria FC Academy.

Require five-job CI before merge to Lefteria-FC-OS/main. No live deployment or edits to the original.
