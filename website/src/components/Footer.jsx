import React from 'react';
import { ArrowUp, Heart, BookOpen, ExternalLink } from 'lucide-react';

export default function Footer({ onOpenDonate }) {
  const scrollToTop = () => {
    window.scrollTo({ top: 0, behavior: 'smooth' });
  };

  return (
    <footer className="py-20 border-t border-white/[0.08] relative bg-[#040407] overflow-hidden">
      <div className="max-w-7xl mx-auto px-4 sm:px-6 lg:px-8">
        
        {/* Massive Editorial Background Wordmark */}
        <div className="select-none pointer-events-none text-center font-display font-extrabold text-[12vw] tracking-tighter leading-none text-white/[0.02] mb-8">
          YTMUSICWP
        </div>

        <div className="grid grid-cols-1 md:grid-cols-12 gap-10 pb-16 border-b border-white/[0.06]">
          
          {/* Brand info */}
          <div className="md:col-span-5 flex flex-col justify-between">
            <div>
              <div className="flex items-center gap-3 mb-3">
                <div className="w-8 h-8 rounded-xl overflow-hidden border border-white/20 bg-black flex items-center justify-center">
                  <img src="./logo.png" alt="YTMusicWP Logo" className="w-full h-full object-cover" />
                </div>
                <h3 className="font-display font-bold text-2xl text-white">
                  YTMusic<span className="text-[#00F0FF]">WP</span>
                </h3>
              </div>
              <p className="text-slate-400 text-sm font-light leading-relaxed max-w-sm">
                The open-source, community-driven YouTube Music client dedicated to keeping legendary Lumia smartphones alive with modern audio capabilities.
              </p>
            </div>
            
            <div className="mt-8 flex items-center gap-2 text-xs font-mono text-slate-500">
              <span>DESIGNED BY YASUKO (AN)</span>
              <span>&bull;</span>
              <span>MPL 2.0 LICENSE</span>
            </div>
          </div>

          {/* Quick Links */}
          <div className="md:col-span-3 font-mono text-xs flex flex-col gap-3">
            <span className="text-white font-semibold tracking-wider uppercase mb-1">NAVIGATION</span>
            <a href="#features" className="text-slate-400 hover:text-white transition-colors">01/ Features</a>
            <a href="#gallery" className="text-slate-400 hover:text-white transition-colors">02/ Gallery</a>
            <a href="#devices" className="text-slate-400 hover:text-white transition-colors">03/ Devices</a>
            <a href="#install" className="text-slate-400 hover:text-white transition-colors">04/ Installation</a>
            <button onClick={onOpenDonate} className="text-amber-400 hover:text-amber-300 text-left transition-colors">
              05/ Support Project
            </button>
          </div>

          {/* External Links */}
          <div className="md:col-span-4 font-mono text-xs flex flex-col gap-3">
            <span className="text-white font-semibold tracking-wider uppercase mb-1">COMMUNITY & SOURCE</span>
            <a 
              href="https://github.com/Yasukoisreal/YTMusicWP" 
              target="_blank" 
              rel="noreferrer"
              className="text-slate-400 hover:text-white transition-colors flex items-center gap-1.5"
            >
              <span>GitHub Repository</span>
              <ExternalLink className="w-3 h-3 text-slate-500" />
            </a>
            <a 
              href="https://github.com/Yasukoisreal/YTMusicWP/wiki" 
              target="_blank" 
              rel="noreferrer"
              className="text-slate-400 hover:text-white transition-colors flex items-center gap-1.5"
            >
              <span>Official Project Wiki</span>
              <ExternalLink className="w-3 h-3 text-slate-500" />
            </a>
            <a 
              href="https://store.live.net.co/app/447" 
              target="_blank" 
              rel="noreferrer"
              className="text-slate-400 hover:text-white transition-colors flex items-center gap-1.5"
            >
              <span>Download on Live Store</span>
              <ExternalLink className="w-3 h-3 text-slate-500" />
            </a>
            <a 
              href="https://github.com/Yasukoisreal/YTMusicWP/issues" 
              target="_blank" 
              rel="noreferrer"
              className="text-slate-400 hover:text-white transition-colors flex items-center gap-1.5"
            >
              <span>Report an Issue / Feedback</span>
              <ExternalLink className="w-3 h-3 text-slate-500" />
            </a>
          </div>

        </div>

        {/* Bottom Bar */}
        <div className="pt-8 flex flex-col sm:flex-row items-center justify-between gap-4 font-mono text-xs text-slate-500">
          <p>
            &copy; {new Date().getFullYear()} YTMusicWP. Non-commercial, educational open-source software.
          </p>

          <button
            onClick={scrollToTop}
            className="flex items-center gap-2 px-4 py-2 rounded-full bg-white/[0.04] hover:bg-white/[0.08] text-slate-300 hover:text-white transition-all border border-white/[0.08]"
          >
            <span>BACK TO TOP</span>
            <ArrowUp className="w-3.5 h-3.5" />
          </button>
        </div>

      </div>
    </footer>
  );
}
