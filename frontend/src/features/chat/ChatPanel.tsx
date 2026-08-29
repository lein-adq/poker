import { useState } from "react";
import type { ChatMessageDto } from "../table/types";

export default function ChatPanel({
  messages,
  onSend,
}: {
  messages: ChatMessageDto[];
  onSend: (message: string) => void;
}) {
  const [draft, setDraft] = useState("");

  function submit(e: React.FormEvent) {
    e.preventDefault();
    if (!draft.trim()) return;
    onSend(draft.trim());
    setDraft("");
  }

  return (
    <div className="chat-panel">
      <div className="chat-messages">
        {messages.map((m, i) => (
          <div key={i} className="chat-message">
            <span className="chat-user">
              {m.userId.slice(0, 8)}
              {m.isSpectator && <span className="badge spectator-badge">spectator</span>}
            </span>
            : {m.message}
          </div>
        ))}
      </div>
      <form onSubmit={submit} className="chat-input">
        <input
          value={draft}
          onChange={(e) => setDraft(e.target.value)}
          placeholder="Say something…"
          maxLength={500}
        />
        <button type="submit">Send</button>
      </form>
    </div>
  );
}
