import React, { useState, useEffect } from 'react';
import { Download, ExternalLink, Sparkles, BookOpen } from 'lucide-react';
import GithubIcon from './GithubIcon';

export default function Navbar({ onOpenDonate }) {
  const [scrolled, setScrolled] = useState(false);

  useEffect(() => {
    const handleScroll = () => {
      setScrolled(window.scrollY > 40);
    };
    window.addEventListener('scroll', handleScroll);
    return () => window.removeEventListener('scroll', handleScroll);
  }, []);

  return (
    <header className={`fixed top-0 left-0 right-0 z-50 transition-all duration-300 ${scrolled ? 'py-3' : 'py-5'}`}>
      <div className="max-w-7xl mx-auto px-4 sm:px-6 lg:px-8">
        <nav className="glass-panel rounded-full px-5 py-3 flex items-center justify-between border border-white/10 shadow-2xl backdrop-blur-xl">
          {/* Logo / Brand */}
          <a href="#" className="flex items-center gap-3 group">
            <div className="relative w-8 h-8 rounded-xl overflow-hidden border border-white/20 shadow-md shadow-[#00F0FF]/10 group-hover:border-[#00F0FF]/60 transition-all duration-300 flex items-center justify-center bg-black">
              <img 
                src="./logo.png" 
                alt="YTMusicWP Logo" 
                className="w-full h-full object-cover transition-transform duration-500 group-hover:scale-110" 
              />
            </div>
            <div className="flex flex-col">
              <span className="font-display font-bold text-lg tracking-tight text-white group-hover:text-[#00F0FF] transition-colors">
                YTMusic<span className="text-[#00F0FF]">WP</span>
              </span>
            </div>
            <span className="hidden sm:inline-block px-2 py-0.5 text-[10px] font-mono tracking-wider bg-white/5 border border-white/10 rounded-full text-slate-300">
              v2.3.0
            </span>
          </a>

          {/* Nav Links */}
          <div className="hidden md:flex items-center gap-6 font-mono text-xs text-slate-400">
            <a href="#features" className="hover:text-white transition-colors tracking-wider">
              <span className="text-[#00F0FF] mr-1">01/</span>FEATURES
            </a>
            <a href="#gallery" className="hover:text-white transition-colors tracking-wider">
              <span className="text-[#00F0FF] mr-1">02/</span>GALLERY
            </a>
            <a href="#devices" className="hover:text-white transition-colors tracking-wider">
              <span className="text-[#00F0FF] mr-1">03/</span>DEVICES
            </a>
            <a href="#install" className="hover:text-white transition-colors tracking-wider">
              <span className="text-[#00F0FF] mr-1">04/</span>INSTALL
            </a>
            <a 
              href="https://github.com/Yasukoisreal/YTMusicWP/wiki" 
              target="_blank" 
              rel="noreferrer"
              className="hover:text-[#00F0FF] transition-colors flex items-center gap-1"
            >
              <BookOpen className="w-3.5 h-3.5" />
              WIKI
            </a>
          </div>

          {/* Action CTAs */}
          <div className="flex items-center gap-3">
            <button
              onClick={onOpenDonate}
              className="hidden sm:flex items-center gap-1.5 px-3 py-1.5 text-xs font-mono text-amber-300 bg-amber-500/10 hover:bg-amber-500/20 border border-amber-500/30 rounded-full transition-all"
            >
              <Sparkles className="w-3.5 h-3.5" />
              <span>DONATE</span>
            </button>

            <a
              href="https://github.com/Yasukoisreal/YTMusicWP"
              target="_blank"
              rel="noreferrer"
              className="p-2 text-slate-400 hover:text-white hover:bg-white/10 rounded-full transition-all"
              title="GitHub Repository"
            >
              <GithubIcon className="w-4 h-4" />
            </a>

            <a
              href="https://github.com/Yasukoisreal/YTMusicWP/releases"
              target="_blank"
              rel="noreferrer"
              className="flex items-center gap-1.5 px-4 py-1.5 text-xs font-semibold text-black bg-[#00F0FF] hover:bg-[#38f6ff] rounded-full shadow-lg shadow-[#00F0FF]/20 hover:shadow-[#00F0FF]/40 transition-all active:scale-95"
            >
              <Download className="w-3.5 h-3.5" />
              <span>GET APPX</span>
            </a>
          </div>
        </nav>
      </div>
    </header>
  );
}
