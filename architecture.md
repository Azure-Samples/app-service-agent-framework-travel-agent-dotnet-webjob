# Travel Planner Architecture

## High-Level System Overview

```mermaid
flowchart TB
    User[User]
    UI[Web UI - Static HTML]
    AppService[App Service P0v4 - .NET 9.0 API + Background Worker]
    ServiceBus[Service Bus - Async Queue]
    Cosmos[Cosmos DB - Task Status & Results]
    AI[Azure AI Foundry - GPT-4o + Agent Framework]

    User -->|1. Submit Request| UI
    UI -->|2. POST /api/travel-plans| AppService
    AppService -->|3. Queue Message| ServiceBus
    AppService -->|4. Store Status| Cosmos
    AppService -->|5. Return TaskId| UI
    UI -->|6. Poll Status| AppService
    ServiceBus -->|7. Process Message| AppService
    AppService -->|8. Generate Plan| AI
    AI -->|9. Return Itinerary| AppService
    AppService -->|10. Save Result| Cosmos
    Cosmos -->|11. Return Complete| UI
    UI -->|12. Display| User

    style User fill:#e1f5ff
    style UI fill:#e1f5ff
    style AppService fill:#fff4e1
    style ServiceBus fill:#ffe1f5
    style Cosmos fill:#e1ffe1
    style AI fill:#f5e1ff
```

## How It Works

### 1. **User Submits Travel Request**
User fills out form with destination, dates, budget, interests, and preferences.

### 2. **API Creates Async Task**
- API creates task in Cosmos DB with status "queued"
- Sends message to Service Bus queue
- Returns taskId immediately (non-blocking)

### 3. **Background Processing**
- Background worker picks up message from queue
- Updates task status to "processing"
- Calls Azure AI Foundry to generate itinerary

### 4. **AI Agent Generation**
- Creates persistent AI agent with GPT-4o
- Runs agent with travel instructions and user preferences
- Agent generates detailed itinerary with activities, costs, tips

### 5. **Store and Return Results**
- Parses AI response and extracts travel tips
- Updates Cosmos DB with completed itinerary
- Task status changes to "completed"

### 6. **UI Polling and Display**
- UI polls API every second for status updates
- Shows progress bar during processing
- Displays formatted itinerary when complete

## Key Architecture Patterns

### ✅ Async Request-Reply Pattern
- API returns immediately with taskId
- Client polls for status updates
- No long-running HTTP connections

### ✅ Background Processing
- Service Bus decouples API from heavy AI work
- Enables horizontal scaling
- Retry logic for reliability

### ✅ Azure AI Agent Framework
- Server-side persistent agents with threads
- Managed lifecycle (create → run → delete)
- Conversation context via threads

### ✅ Managed Identity
- No credentials in code
- Secure authentication to all Azure services

### ✅ State Management
- Cosmos DB stores all task state
- 24-hour TTL for automatic cleanup

## Key Features

### Asynchronous Processing
- **Request-Reply Pattern**: Client submits request → receives taskId → polls for status
- **Background Processing**: Service Bus ensures reliable async execution
- **Progress Tracking**: Real-time status updates (queued → processing → completed)

### Azure AI Agent Framework
- **Persistent Agents**: Server-side agents with threads and runs
- **Managed Lifecycle**: Agents created per request and cleaned up after use
- **Conversation Context**: Thread-based conversation management
- **AI Orchestration**: GPT-4o model with custom instructions

### State Management
- **Cosmos DB**: Centralized state storage with TTL for auto-cleanup
- **Task Status**: Tracks progress and stores results
- **24-Hour TTL**: Automatic cleanup of old travel plans

### Scalability
- **Premium App Service**: P0v4 tier for production workloads
- **Service Bus**: Decouples API from processing for horizontal scaling
- **Managed Identity**: Secure, credential-less authentication
- **Background Worker**: Dedicated service for heavy AI processing

### Reliability
- **Retry Logic**: Service Bus max 3 delivery attempts
- **Error Handling**: Comprehensive try-catch with logging
- **Status Tracking**: Detailed progress and error messages
- **Cleanup**: Automatic agent deletion and document expiration
