import React, {useState, useEffect} from "react";

export default function App() {
  const [input, setInput] = useState("");
  const [corrected, setCorrected] = useState(null);
  const [answer, setAnswer] = useState(null);
  const [history, setHistory] = useState([]);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState(null);

  useEffect(()=>{ loadHistory(); }, []);

  async function loadHistory() {
    try {
      const res = await fetch("http://localhost:5092/history");
      if (!res.ok) throw new Error("History fetch failed: "+res.status);
      const data = await res.json();
      setHistory(data.reverse());
    } catch(e){ setError(String(e)); }
  }

  async function handleSubmit(e){
    e.preventDefault();
    setError(null);
    setCorrected(null);
    setAnswer(null);
    setLoading(true);
    try {
      const corrReq = { capability: "correct_text", input: { text: input } };
      const corrResp = await fetch("http://localhost:5091/invoke", {
        method:"POST",
        headers: { "Content-Type":"application/json", "Authorization":"Bearer demo-token" },
        body: JSON.stringify(corrReq)
      });
      if (!corrResp.ok) throw new Error("Corrector failed: "+corrResp.status);
      const corrJson = await corrResp.json();
      const correctedText = corrJson?.output?.corrected ?? input;
      setCorrected(correctedText);

      const respReq = { capability:"answer", input:{ text: correctedText }, context:{ original: input } };
      const resp = await fetch("http://localhost:5092/invoke", {
        method:"POST",
        headers: { "Content-Type":"application/json", "Authorization":"Bearer demo-token" },
        body: JSON.stringify(respReq)
      });
      if (!resp.ok) {
        const txt = await resp.text();
        throw new Error("Responder failed: "+resp.status+" "+txt);
      }
      const respJson = await resp.json();
      const answerText = respJson?.output?.answer ?? "(no answer)";
      setAnswer(answerText);
      await loadHistory();
    } catch(err){ setError(String(err)); }
    setLoading(false);
  }

  return (
    <div style={{fontFamily:'system-ui,Segoe UI,Roboto', maxWidth:900, margin:'24px auto'}}>
      <h1>A2A Demo UI — Corrector → Responder</h1>
      <form onSubmit={handleSubmit}>
        <textarea value={input} onChange={e=>setInput(e.target.value)} rows={4} style={{width:'100%',padding:8}} placeholder="Type your question or statement (e.g. 'teh capital of france?')"></textarea>
        <div style={{marginTop:8}}>
          <button type="submit" disabled={loading || !input.trim()} style={{padding:'8px 12px'}}> {loading ? 'Processing...' : 'Send'}</button>
          <button type="button" onClick={()=>{ setInput(''); setCorrected(null); setAnswer(null); setError(null); }} style={{marginLeft:8}}>Clear</button>
        </div>
      </form>

      {error && <div style={{marginTop:12,color:'crimson'}}>Error: {error}</div>}

      <div style={{display:'flex',gap:16,marginTop:16}}>
        <div style={{flex:1,padding:10,border:'1px solid #ddd'}}>
          <h3>Corrected Text</h3>
          <div>{corrected ?? <em>(no result)</em>}</div>
        </div>
        <div style={{flex:1,padding:10,border:'1px solid #ddd'}}>
          <h3>Final Answer</h3>
          <div style={{whiteSpace:'pre-wrap'}}>{answer ?? <em>(no result)</em>}</div>
        </div>
      </div>

      <div style={{marginTop:20}}>
        <h2>History</h2>
        <button onClick={loadHistory} style={{marginBottom:8}}>Refresh</button>
        {history.length===0 && <div><em>No history yet.</em></div>}
        {history.map((h,idx)=> (
          <div key={idx} style={{border:'1px solid #eee',padding:8,marginBottom:8}}>
            <div style={{fontSize:12,color:'#666'}}>{new Date(h.timestamp).toLocaleString()}</div>
            <div><strong>Original:</strong> {h.original}</div>
            <div><strong>Corrected:</strong> {h.corrected}</div>
            <div><strong>Answer:</strong><div style={{whiteSpace:'pre-wrap'}}>{h.answer}</div></div>
          </div>
        ))}
      </div>
    </div>
  );
}
