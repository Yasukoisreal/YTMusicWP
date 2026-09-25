import React, { useState } from 'react';
import Navbar from './components/Navbar';
import Hero from './components/Hero';
import InteractivePlayerPreview from './components/InteractivePlayerPreview';
import FeatureShowcase from './components/FeatureShowcase';
import ScreenshotGallery from './components/ScreenshotGallery';
import DeviceMatrix from './components/DeviceMatrix';
import InstallGuide from './components/InstallGuide';
import DonationModal from './components/DonationModal';
import Footer from './components/Footer';

export default function App() {
  const [donateOpen, setDonateOpen] = useState(false);

  return (
    <div className="relative min-h-screen bg-[#06060A] text-[#F1F3F9] font-sans selection:bg-[#00F0FF] selection:text-black">
      {/* Tactile Analog Film Grain Overlay */}
      <div className="pointer-events-none fixed inset-0 grain-overlay z-40" />

      {/* Floating Header */}
      <Navbar onOpenDonate={() => setDonateOpen(true)} />

      {/* Main Content Sections */}
      <main>
        <Hero />
        <InteractivePlayerPreview />
        <FeatureShowcase />
        <ScreenshotGallery />
        <DeviceMatrix />
        <InstallGuide />
      </main>

      {/* Footer */}
      <Footer onOpenDonate={() => setDonateOpen(true)} />

      {/* Tactile Donation Modal */}
      <DonationModal isOpen={donateOpen} onClose={() => setDonateOpen(false)} />
    </div>
  );
}
