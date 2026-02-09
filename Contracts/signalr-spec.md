# SignalR Contract: Messaging Hub

**MMP Status**: BASIC messaging only (send/receive), advanced features deferred to Phase 2

## Hub Route
`/hubs/messages`

## Connection Requirements
- JWT access token passed via `Authorization: Bearer` header OR query string `?access_token={token}`
- Client must reconnect within 30 seconds to maintain session
- Heartbeat interval: 15 seconds; disconnect after 30 seconds of inactivity
- User automatically added to user-specific group for targeted messaging

## Client-to-Server Methods (BASIC - MMP)

| Method | Payload | Description | Status |
|--------|---------|-------------|--------|
| `SendMessage` | `{ matchId: guid, body: string }` | Publishes text message to match participants | ✅ **MMP** |
| `Acknowledge` | `{ messageId: guid }` | Confirms reception for delivery tracking (basic implementation) | ✅ **MMP** |

## Client-to-Server Methods (DEFERRED to Phase 2)

| Method | Payload | Description | Status |
|--------|---------|-------------|--------|
| `Typing` | `{ matchId: guid, isTyping: bool }` | Broadcasts typing state for UI presence | ⏸️ Deferred |

## Server-to-Client Methods (BASIC - MMP)

| Method | Payload | Description | Status |
|--------|---------|-------------|--------|
| `MessageReceived` | `MessageDto` | Delivers new message to connected clients (both sender & receiver) | ✅ **MMP** |

## Server-to-Client Methods (DEFERRED to Phase 2)

| Method | Payload | Description | Status |
|--------|---------|-------------|--------|
| `MessageUpdated` | `MessageDto` | Signals read/delivery status changes | ⏸️ Deferred |
| `TypingChanged` | `{ matchId: guid, userId: string, isTyping: bool }` | Updates typing indicator | ⏸️ Deferred |
| `PresenceChanged` | `{ userId: string, state: "Online" \| "Offline" }` | Broadcasts participant presence | ⏸️ Deferred |
| `MatchArchived` | `{ matchId: guid, reason: string }` | Notifies clients when match is blocked/archived | ⏸️ Deferred |

## MessageDto Schema (BASIC)
```json
{
  "messageId": "guid",
  "matchId": "guid",
  "senderId": "string (Keycloak sub claim)",
  "body": "string (max 1000 chars)",
  "bodyType": "Text",
  "sentAt": "2026-01-26T14:30:00Z",
  "deliveredAt": null,
  "readAt": null,
  "moderationFlag": null
}
```

**MMP Simplifications**:
- Basic text messages only (no images/media in Phase 1)
- Basic acknowledgment (read receipts deferred)
- No typing indicators
- No presence/online status
- deliveredAt, readAt, moderationFlag fields present but null (used in Phase 2)

## Error Handling
- Server responds with `HubException` containing error code
- Common codes:
  - `authentication-required` - No valid JWT token
  - `not-authorized` - User not a participant in match
  - `message-too-long` - Body exceeds 1000 characters
  - `content-blocked` - Message flagged by content moderation
  - `messaging-blocked` - Users have blocked each other
  - `send-failed` - Generic send error (check logs)
- Clients must retry `SendMessage` after transient failures with exponential backoff (max 3 attempts)

## Security Considerations
- All payloads validated against match ownership via MatchmakingService API
- Messages scanned by content moderation service before dispatch
- Block status checked before message delivery (P0 safety requirement)
- Audit events emitted to structured logs with correlation ID `messageId`
- User ID extracted from JWT claims (ClaimTypes.NameIdentifier or "sub")

## MMP Implementation Notes
- Persistence: Messages stored in MessagingService database with match-based conversation grouping
- Match Verification: Calls MatchmakingService `/api/matchmaking/matches/{userId}` to verify participation
- Safety Integration: Calls SafetyService to check block status before delivery
- Delivery: SignalR groups used for targeted message delivery (`Clients.User(receiverId)`)
- Fallback: Offline users receive messages on reconnection via conversation history API

## Future Enhancements (Phase 2)
- Read receipts and delivery tracking
- Typing indicators and presence
- Message reactions/emojis
- Media attachments (images, voice notes)
- Message editing/deletion with tombstone markers
- Push notifications for offline users via Firebase/APNs
