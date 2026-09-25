import React, { useState } from 'react';
import { X, Copy, Check, Heart, Coffee, CreditCard, Sparkles } from 'lucide-react';
import confetti from 'canvas-confetti';

export default function DonationModal({ isOpen, onClose }) {
  const [copied, setCopied] = useState(false);

  if (!isOpen) return null;

  const handleCopy = () => {
    navigator.clipboard.writeText('700652007');
    setCopied(true);
    confetti({
      particleCount: 50,
      spread: 60,
      origin: { y: 0.7 }
    });
    setTimeout(() => setCopied(false), 2500);
  };

  return (
    <div className="fixed inset-0 z-50 bg-black/85 backdrop-blur-xl flex items-center justify-center p-4">
      <div className="relative w-full max-w-md bg-[#0E0E16] rounded-3xl p-6 sm:p-8 border border-white/20 shadow-2xl flex flex-col">
        
        {/* Close Button */}
        <button
          onClick={onClose}
          className="absolute top-5 right-5 p-2 rounded-full bg-white/5 hover:bg-white/10 text-slate-400 hover:text-white transition-colors"
        >
          <X className="w-5 h-5" />
        </button>

        {/* Modal Header */}
        <div className="flex items-center gap-3 mb-6">
          <div className="w-10 h-10 rounded-xl bg-amber-500/10 border border-amber-500/20 flex items-center justify-center text-amber-400">
            <Heart className="w-5 h-5 fill-amber-400" />
          </div>
          <div>
            <h3 className="font-display font-bold text-xl text-white">Support YTMusicWP</h3>
            <p className="text-xs text-slate-400 font-mono">Breathe life into legacy Windows devices</p>
          </div>
        </div>

        {/* QR Code */}
        <div className="p-3 bg-white rounded-2xl mb-6 shadow-inner flex justify-center">
          <img
            src="./Pictures/donate_qr.jpg"
            alt="Donate QR MB Bank"
            className="w-56 h-auto rounded-xl object-contain"
          />
        </div>

        {/* Bank Details */}
        <div className="p-4 rounded-xl bg-white/[0.03] border border-white/[0.08] mb-6 flex items-center justify-between">
          <div className="flex flex-col">
            <span className="text-[10px] font-mono text-slate-500 uppercase">MB BANK (VIETNAM)</span>
            <span className="font-mono text-base font-bold text-white tracking-wider">700652007</span>
            <span className="text-xs text-slate-400">NGUYEN TRUONG AN</span>
          </div>

          <button
            onClick={handleCopy}
            className="flex items-center gap-1.5 px-3 py-2 rounded-lg bg-white/10 hover:bg-white/20 text-xs font-mono font-medium text-white transition-all active:scale-95"
          >
            {copied ? <Check className="w-4 h-4 text-emerald-400" /> : <Copy className="w-4 h-4 text-[#00F0FF]" />}
            <span>{copied ? 'COPIED' : 'COPY'}</span>
          </button>
        </div>

        {/* International Options */}
        <div className="grid grid-cols-2 gap-3">
          <a
            href="https://buymeacoffee.com/yasukoisreal"
            target="_blank"
            rel="noreferrer"
            className="flex items-center justify-center gap-2 py-3 px-4 rounded-xl bg-[#FFDD00] text-black font-semibold text-xs hover:bg-[#FFE433] transition-all"
          >
            <Coffee className="w-4 h-4" />
            <span>Buy me a Coffee</span>
          </a>

          <a
            href="https://paypal.me/yasukoisreal"
            target="_blank"
            rel="noreferrer"
            className="flex items-center justify-center gap-2 py-3 px-4 rounded-xl bg-[#0070BA] text-white font-semibold text-xs hover:bg-[#0079C1] transition-all"
          >
            <CreditCard className="w-4 h-4" />
            <span>PayPal</span>
          </a>
        </div>

      </div>
    </div>
  );
}
