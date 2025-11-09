A2A-AgentA2A Backend - Corrector, Responder, Caller (MCP + LLM)

Prereqs:
 - .NET 9 SDK
 - Node 18+ (node on PATH)
 - Ollama CLI (if using local LLM)
 - Pull model: ollama pull gemma3:1b

Run:
1) Start CorrectorAgent (spawns MCP tool):
   cd backend/CorrectorAgent
   dotnet run

2) Start ResponderAgent (set provider env first):
   $env:LLM_PROVIDER='ollama'; $env:LLM_MODEL='gemma3:1b'; $env:LLM_ENDPOINT='http://localhost:11434'
   cd backend/ResponderAgent
   dotnet run

3) Start CallerAgent (console):
   cd backend/CallerAgent
   dotnet run
