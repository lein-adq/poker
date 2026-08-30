import { useState, useRef, useEffect } from "react";
import type { ChatMessageDto } from "../table/types";
import EmojiPicker, { Theme } from "emoji-picker-react";

export default function ChatPanel({
  messages,
  onSend,
}: {
  messages: ChatMessageDto[];
  onSend: (message: string) => void;
}) {
  const [draft, setDraft] = useState("");
  const [showEmoji, setShowEmoji] = useState(false);
  const messagesEndRef = useRef<HTMLDivElement>(null);
  const emojiRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    messagesEndRef.current?.scrollIntoView({ behavior: "smooth" });
  }, [messages]);

  useEffect(() => {
    function handleClickOutside(event: MouseEvent) {
      if (emojiRef.current && !emojiRef.current.contains(event.target as Node)) {
        setShowEmoji(false);
      }
    }
    if (showEmoji) {
      document.addEventListener("mousedown", handleClickOutside);
    }
    return () => document.removeEventListener("mousedown", handleClickOutside);
  }, [showEmoji]);

  function submit(e: React.FormEvent) {
    e.preventDefault();
    if (!draft.trim()) return;
    onSend(draft.trim());
    setDraft("");
    setShowEmoji(false);
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
        <div ref={messagesEndRef} />
      </div>
      
      <div style={{ position: "relative" }} ref={emojiRef}>
        {showEmoji && (
          <div style={{ position: "absolute", bottom: "100%", right: 0, zIndex: 1000, marginBottom: "0.5rem" }}>
            <EmojiPicker 
              theme={Theme.DARK} 
              onEmojiClick={(emojiData) => setDraft(d => d + emojiData.emoji)} 
            />
          </div>
        )}
        <form onSubmit={submit} className="chat-input" style={{ display: 'flex', gap: '0.5rem', width: '100%' }}>
          <button 
            type="button" 
            onClick={() => setShowEmoji(!showEmoji)} 
            style={{ background: '#2a2f36', padding: '0 0.5rem', fontSize: '1.2rem', borderRadius: '4px' }}
            title="Add emoji"
          >
            😀
          </button>
          <input
            value={draft}
            onChange={(e) => setDraft(e.target.value)}
            placeholder="Say something…"
            maxLength={500}
            autoFocus={!showEmoji}
          />
          <button type="submit">Send</button>
        </form>
      </div>
    </div>
  );
}
