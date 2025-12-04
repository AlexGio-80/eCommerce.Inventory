import { Component, ElementRef, OnDestroy, OnInit, ViewChild } from '@angular/core';
import { GradingService, GradingResult } from '../../../../core/services/grading.service';
import { MatSnackBar } from '@angular/material/snack-bar';

interface CapturedImage {
    dataUrl: string;
    label: string;
}

@Component({
    selector: 'app-grading-page',
    templateUrl: './grading-page.component.html',
    styleUrls: ['./grading-page.component.scss'],
    standalone: false
})
export class GradingPageComponent implements OnInit, OnDestroy {
    @ViewChild('videoElement') videoElement!: ElementRef<HTMLVideoElement>;
    @ViewChild('canvasElement') canvasElement!: ElementRef<HTMLCanvasElement>;

    stream: MediaStream | null = null;
    capturedImages: CapturedImage[] = [];
    gradingResult: GradingResult | null = null;
    isLoading = false;
    error: string | null = null;
    currentLabel = 'Fronte';

    constructor(
        private gradingService: GradingService,
        private snackBar: MatSnackBar
    ) { }

    ngOnInit(): void {
        this.startCamera();
    }

    ngOnDestroy(): void {
        this.stopCamera();
    }

    async startCamera() {
        try {
            this.stream = await navigator.mediaDevices.getUserMedia({ video: { facingMode: 'environment' } });
            if (this.videoElement) {
                this.videoElement.nativeElement.srcObject = this.stream;
            }
            this.error = null;
        } catch (err) {
            console.error('Error accessing camera:', err);
            this.error = 'Impossibile accedere alla fotocamera. Assicurati di aver concesso i permessi.';
            this.snackBar.open(this.error, 'Chiudi', { duration: 5000 });
        }
    }

    stopCamera() {
        if (this.stream) {
            this.stream.getTracks().forEach(track => track.stop());
            this.stream = null;
        }
    }

    captureImage() {
        if (!this.videoElement || !this.canvasElement) return;

        const video = this.videoElement.nativeElement;
        const canvas = this.canvasElement.nativeElement;
        const context = canvas.getContext('2d');

        if (context) {
            canvas.width = video.videoWidth;
            canvas.height = video.videoHeight;
            context.drawImage(video, 0, 0, canvas.width, canvas.height);

            const dataUrl = canvas.toDataURL('image/jpeg');
            this.capturedImages.push({
                dataUrl,
                label: this.currentLabel
            });

            // Update label for next capture
            if (this.capturedImages.length === 1) {
                this.currentLabel = 'Retro';
            } else {
                this.currentLabel = `Foto ${this.capturedImages.length + 1}`;
            }
        }
    }

    removeImage(index: number) {
        this.capturedImages.splice(index, 1);
        this.updateLabels();
    }

    updateLabels() {
        if (this.capturedImages.length === 0) {
            this.currentLabel = 'Fronte';
        } else if (this.capturedImages.length === 1) {
            this.currentLabel = 'Retro';
        } else {
            this.currentLabel = `Foto ${this.capturedImages.length + 1}`;
        }
    }

    retakeAll() {
        this.capturedImages = [];
        this.gradingResult = null;
        this.currentLabel = 'Fronte';
        this.startCamera();
    }

    async analyzeImages() {
        if (this.capturedImages.length === 0) return;

        this.isLoading = true;

        try {
            // Convert all DataURLs to Files
            const files: File[] = [];
            for (let i = 0; i < this.capturedImages.length; i++) {
                const img = this.capturedImages[i];
                const res = await fetch(img.dataUrl);
                const blob = await res.blob();
                files.push(new File([blob], `card-${img.label.toLowerCase()}.jpg`, { type: 'image/jpeg' }));
            }

            this.gradingService.analyzeCardMultiple(files).subscribe({
                next: (result) => {
                    this.gradingResult = result;
                    this.isLoading = false;
                    this.stopCamera();
                },
                error: (err) => {
                    console.error('Grading error:', err);
                    this.snackBar.open('Errore durante l\'analisi della carta.', 'Chiudi', { duration: 3000 });
                    this.isLoading = false;
                }
            });
        } catch (err) {
            console.error('Error converting images:', err);
            this.isLoading = false;
        }
    }

    onFilesSelected(event: Event) {
        const input = event.target as HTMLInputElement;
        if (input.files && input.files.length > 0) {
            for (let i = 0; i < input.files.length; i++) {
                const file = input.files[i];
                const reader = new FileReader();
                reader.onload = (e) => {
                    this.capturedImages.push({
                        dataUrl: e.target?.result as string,
                        label: this.currentLabel
                    });
                    this.updateLabels();
                };
                reader.readAsDataURL(file);
            }
        }
    }

    getConditionClass(): string {
        if (!this.gradingResult) return '';
        switch (this.gradingResult.conditionCode) {
            case 'NM': return 'condition-nm';
            case 'SP': return 'condition-sp';
            case 'MP': return 'condition-mp';
            case 'PL': return 'condition-pl';
            case 'PO': return 'condition-po';
            default: return '';
        }
    }
}
