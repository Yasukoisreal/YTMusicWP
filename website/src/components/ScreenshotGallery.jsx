import React, { useState } from 'react';
import { Image, X, ChevronLeft, ChevronRight, Maximize2 } from 'lucide-react';

const screenshots = [
  { src: './Pictures/02.png', title: 'Apple Music UI', category: 'Player', desc: 'Blurred backdrop powered by Lumia Imaging SDK' },
  { src: './Pictures/01.png', title: 'Spotify Classic Dark', category: 'Player', desc: 'OLED AMOLED black player with green accents' },
  { src: './Pictures/03.png', title: 'Synced Karaoke Lyrics', category: 'Player', desc: 'Real-time scrolling with progressive opacity falloff' },
  { src: './Pictures/04.png', title: 'Dynamic Home Feed', category: 'Explore', desc: 'Quick Picks, Moods & Genres with pull-to-refresh' },
  { src: './Pictures/05.png', title: 'SimpMusic Library', category: 'Library', desc: '4 quick-access tiles with album art mosaic' },
  { src: './Pictures/06.png', title: 'Listen Together Rooms', category: 'Social', desc: 'Real-time playback sync with Metrolist & SimpMusic' },
  { src: './Pictures/07.png', title: 'Smart Offline Downloads', category: 'Library', desc: 'M4A atom metadata and high-res artwork injection' },
  { src: './Pictures/08.png', title: 'Search & Suggestions', category: 'Explore', desc: 'Live YouTube Music entity search and query suggestions' },
  { src: './Pictures/09.png', title: 'Start Screen Live Tiles', category: 'System', desc: 'Flip Tiles and People-Hub style artist mosaic' },
  { src: './Pictures/10.png', title: 'Detailed Song Credits', category: 'Player', desc: 'Complete performer, composer, and licensing info' },
  { src: './Pictures/11.png', title: 'Queue & Infinite Radio', category: 'Player', desc: 'Smart queue with automatic endless recommendations' },
  { src: './Pictures/12.png', title: 'QR Code Device Login', category: 'System', desc: 'Fast Google account sync without browser limits' },
  { src: './Pictures/13.png', title: 'Playback Speed Control', category: 'Player', desc: 'Fine-tuned audio playback speeds from 0.5x to 2.0x' },
];

const categories = ['All', 'Player', 'Library', 'Explore', 'System'];

export default function ScreenshotGallery() {
  const [selectedCategory, setSelectedCategory] = useState('All');
  const [activeModalImage, setActiveModalImage] = useState(null);

  const filteredScreenshots = selectedCategory === 'All'
    ? screenshots
    : screenshots.filter(s => s.category === selectedCategory);

  return (
    <section id="gallery" className="py-28 border-b border-white/[0.08] relative bg-[#06060A]">
      <div className="max-w-7xl mx-auto px-4 sm:px-6 lg:px-8">
        
        {/* Header */}
        <div className="flex flex-col md:flex-row md:items-end justify-between mb-12 gap-6">
          <div>
            <div className="inline-flex items-center gap-2 font-mono text-xs text-[#00F0FF] tracking-widest uppercase mb-3">
              <Image className="w-3.5 h-3.5" />
              <span>[03_REAL_DEVICE_SHOWCASE]</span>
            </div>
            <h2 className="font-display font-extrabold text-4xl sm:text-5xl lg:text-6xl text-white tracking-tight">
              CAPTURED ON LUMIA. <br />
              <span className="text-slate-400 font-light">PIXEL PERFECTION.</span>
            </h2>
          </div>

          {/* Filter Pills */}
          <div className="flex flex-wrap gap-2">
            {categories.map((cat) => (
              <button
                key={cat}
                onClick={() => setSelectedCategory(cat)}
                className={`px-4 py-1.5 rounded-full text-xs font-mono transition-all ${
                  selectedCategory === cat
                    ? 'bg-white text-black font-semibold shadow-md'
                    : 'bg-white/[0.04] text-slate-400 hover:text-white border border-white/[0.08]'
                }`}
              >
                {cat}
              </button>
            ))}
          </div>
        </div>

        {/* Gallery Grid */}
        <div className="grid grid-cols-2 sm:grid-cols-3 md:grid-cols-4 lg:grid-cols-5 gap-4 sm:gap-6">
          {filteredScreenshots.map((item, index) => (
            <div
              key={index}
              onClick={() => setActiveModalImage(item)}
              className="group relative aspect-[9/16] bg-[#0E0E14] rounded-2xl overflow-hidden border border-white/[0.08] hover:border-[#00F0FF]/50 transition-all duration-300 cursor-pointer shadow-lg hover:shadow-2xl hover:shadow-[#00F0FF]/10 hover:-translate-y-1"
            >
              <img
                src={item.src}
                alt={item.title}
                className="w-full h-full object-cover object-top transition-transform duration-500 group-hover:scale-105"
                loading="lazy"
              />

              {/* Hover overlay */}
              <div className="absolute inset-0 bg-gradient-to-t from-black/90 via-black/30 to-transparent opacity-0 group-hover:opacity-100 transition-opacity duration-300 flex flex-col justify-end p-4">
                <span className="font-mono text-[9px] text-[#00F0FF] uppercase tracking-wider">{item.category}</span>
                <h4 className="font-semibold text-white text-sm truncate">{item.title}</h4>
                <p className="text-[11px] text-slate-300 line-clamp-2 mt-0.5">{item.desc}</p>
                <div className="mt-2 flex items-center gap-1 text-[10px] font-mono text-[#00F0FF]">
                  <Maximize2 className="w-3 h-3" />
                  <span>ZOOM IN</span>
                </div>
              </div>
            </div>
          ))}
        </div>

      </div>

      {/* Fullscreen Lightbox Modal */}
      {activeModalImage && (
        <div className="fixed inset-0 z-50 bg-black/90 backdrop-blur-2xl flex items-center justify-center p-4">
          <button
            onClick={() => setActiveModalImage(null)}
            className="absolute top-6 right-6 p-3 rounded-full bg-white/10 hover:bg-white/20 text-white transition-all z-10"
          >
            <X className="w-6 h-6" />
          </button>

          <div className="relative max-w-sm w-full aspect-[9/16] max-h-[85vh] rounded-3xl overflow-hidden border border-white/20 shadow-2xl">
            <img
              src={activeModalImage.src}
              alt={activeModalImage.title}
              className="w-full h-full object-contain bg-black"
            />
            <div className="absolute bottom-0 inset-x-0 p-5 bg-gradient-to-t from-black via-black/80 to-transparent">
              <span className="font-mono text-xs text-[#00F0FF] uppercase">{activeModalImage.category}</span>
              <h3 className="font-display font-bold text-xl text-white mt-1">{activeModalImage.title}</h3>
              <p className="text-xs text-slate-300 mt-1">{activeModalImage.desc}</p>
            </div>
          </div>
        </div>
      )}
    </section>
  );
}
