import React, { useState } from 'react';
import { Terminal, Download, ShieldCheck, FileCheck, Check, Copy, ExternalLink, HelpCircle } from 'lucide-react';

export default function InstallGuide() {
  const [activeTab, setActiveTab] = useState('wp81'); // 'wp81' or 'w10m'
  const [copied, setCopied] = useState(false);

  const handleCopyCmd = (text) => {
    navigator.clipboard.writeText(text);
    setCopied(true);
    setTimeout(() => setCopied(false), 2000);
  };

  return (
    <section id="install" className="py-28 border-b border-white/[0.08] relative bg-[#06060A]">
      <div className="max-w-7xl mx-auto px-4 sm:px-6 lg:px-8">
        
        {/* Section Header */}
        <div className="flex flex-col md:flex-row md:items-end justify-between mb-16 gap-6">
          <div>
            <div className="inline-flex items-center gap-2 font-mono text-xs text-[#00F0FF] tracking-widest uppercase mb-3">
              <Terminal className="w-3.5 h-3.5" />
              <span>[05_DEPLOYMENT_GUIDE]</span>
            </div>
            <h2 className="font-display font-extrabold text-4xl sm:text-5xl lg:text-6xl text-white tracking-tight">
              EASY INSTALLATION. <br />
              <span className="text-slate-400 font-light">READY IN MINUTES.</span>
            </h2>
          </div>

          {/* OS Switcher */}
          <div className="flex p-1.5 rounded-full bg-white/[0.04] border border-white/10 backdrop-blur-xl">
            <button
              onClick={() => setActiveTab('wp81')}
              className={`px-5 py-2 rounded-full text-xs font-mono font-semibold transition-all ${
                activeTab === 'wp81'
                  ? 'bg-[#00F0FF] text-black shadow-md'
                  : 'text-slate-400 hover:text-white'
              }`}
            >
              WINDOWS PHONE 8.1
            </button>
            <button
              onClick={() => setActiveTab('w10m')}
              className={`px-5 py-2 rounded-full text-xs font-mono font-semibold transition-all ${
                activeTab === 'w10m'
                  ? 'bg-[#0078D7] text-white shadow-md'
                  : 'text-slate-400 hover:text-white'
              }`}
            >
              WINDOWS 10 MOBILE
            </button>
          </div>
        </div>

        {/* Steps Grid */}
        <div className="grid grid-cols-1 md:grid-cols-3 gap-6 mb-12">
          
          {/* Step 1 */}
          <div className="p-8 rounded-3xl glass-panel border border-white/[0.08] flex flex-col justify-between">
            <div>
              <div className="flex items-center justify-between mb-6">
                <span className="font-mono text-xs text-[#00F0FF]">[STEP_01]</span>
                <Download className="w-5 h-5 text-slate-400" />
              </div>
              <h3 className="font-display font-bold text-xl text-white mb-2">
                Download Release Assets
              </h3>
              <p className="text-sm text-slate-400 font-light leading-relaxed">
                Grab the latest signed <code className="text-[#00F0FF] font-mono">YTMusicWP_v2.3.0.0.appx</code> and root certificate <code className="text-[#00F0FF] font-mono">.cer</code> from the official Releases page.
              </p>
            </div>
            <div className="mt-8 pt-4 border-t border-white/[0.06]">
              <a 
                href="https://github.com/Yasukoisreal/YTMusicWP/releases" 
                target="_blank" 
                rel="noreferrer"
                className="inline-flex items-center gap-1.5 text-xs font-mono text-[#00F0FF] hover:underline"
              >
                <span>OPEN RELEASES PAGE</span>
                <ExternalLink className="w-3 h-3" />
              </a>
            </div>
          </div>

          {/* Step 2 */}
          <div className="p-8 rounded-3xl glass-panel border border-white/[0.08] flex flex-col justify-between">
            <div>
              <div className="flex items-center justify-between mb-6">
                <span className="font-mono text-xs text-[#00F0FF]">[STEP_02]</span>
                <ShieldCheck className="w-5 h-5 text-slate-400" />
              </div>
              <h3 className="font-display font-bold text-xl text-white mb-2">
                Install Certificate (.cer)
              </h3>
              <p className="text-sm text-slate-400 font-light leading-relaxed">
                {activeTab === 'wp81'
                  ? 'Send the .cer file to your Lumia via email or SD Card. Tap the certificate in Mail or File Explorer, then choose "Install Certificate".'
                  : 'Enable Developer Mode in Settings > Update & Security > For developers. You can tap the .cer directly to trust the publisher.'}
              </p>
            </div>
            <div className="mt-8 pt-4 border-t border-white/[0.06] font-mono text-xs text-slate-500">
              TRUSTED ROOT AUTHORITY
            </div>
          </div>

          {/* Step 3 */}
          <div className="p-8 rounded-3xl glass-panel border border-white/[0.08] flex flex-col justify-between">
            <div>
              <div className="flex items-center justify-between mb-6">
                <span className="font-mono text-xs text-[#00F0FF]">[STEP_03]</span>
                <FileCheck className="w-5 h-5 text-slate-400" />
              </div>
              <h3 className="font-display font-bold text-xl text-white mb-2">
                Deploy .appx Package
              </h3>
              <p className="text-sm text-slate-400 font-light leading-relaxed">
                {activeTab === 'wp81'
                  ? 'Connect phone to PC with USB. Use Windows Phone Application Deployment (WPAD) or WPV XAP Deployer to deploy the .appx directly.'
                  : 'Download the .appx directly on your phone using Edge or transfer via USB, tap the file in File Explorer and tap "Install".'}
              </p>
            </div>
            <div className="mt-8 pt-4 border-t border-white/[0.06] font-mono text-xs text-slate-500">
              {activeTab === 'wp81' ? 'TOOLS: WPAD &bull; WPV DEPLOYER' : 'STANDALONE SIDELOAD'}
            </div>
          </div>

        </div>

        {/* Wiki Callout Banner */}
        <div className="p-6 rounded-2xl bg-white/[0.02] border border-white/[0.08] flex flex-col sm:flex-row items-center justify-between gap-4">
          <div className="flex items-center gap-3">
            <HelpCircle className="w-5 h-5 text-[#00F0FF] shrink-0" />
            <p className="text-sm text-slate-300 font-light">
              Need a step-by-step visual tutorial with troubleshooting and tool downloads?
            </p>
          </div>
          <a
            href="https://github.com/Yasukoisreal/YTMusicWP/wiki"
            target="_blank"
            rel="noreferrer"
            className="flex items-center gap-2 px-5 py-2 rounded-full text-xs font-mono font-semibold bg-white text-black hover:bg-[#00F0FF] transition-colors shrink-0"
          >
            <span>READ FULL WIKI GUIDE</span>
            <ExternalLink className="w-3.5 h-3.5" />
          </a>
        </div>

      </div>
    </section>
  );
}
