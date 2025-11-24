import { Directive, ElementRef, HostListener, Input, ComponentRef, ViewContainerRef, ComponentFactoryResolver, Injector, ApplicationRef, EmbeddedViewRef } from '@angular/core';
import { ImagePreviewComponent } from '../components/image-preview/image-preview.component';

@Directive({
    selector: '[appImagePreview]',
    standalone: true
})
export class ImagePreviewDirective {
    @Input('appImagePreview') imageUrl: string | undefined;

    private componentRef: ComponentRef<ImagePreviewComponent> | null = null;

    constructor(
        private elementRef: ElementRef,
        private viewContainerRef: ViewContainerRef,
        private appRef: ApplicationRef
    ) { }

    @HostListener('mouseenter')
    onMouseEnter() {
        if (!this.imageUrl) return;
        this.showPreview();
    }

    @HostListener('mouseleave')
    onMouseLeave() {
        this.hidePreview();
    }

    @HostListener('mousemove', ['$event'])
    onMouseMove(event: MouseEvent) {
        if (this.componentRef) {
            this.updatePosition(event);
        }
    }

    private showPreview() {
        // Create component dynamically
        this.componentRef = this.viewContainerRef.createComponent(ImagePreviewComponent);
        this.componentRef.instance.imageUrl = this.imageUrl!;

        // Append to body to avoid overflow issues
        const domElem = (this.componentRef.hostView as EmbeddedViewRef<any>).rootNodes[0] as HTMLElement;
        document.body.appendChild(domElem);

        // Initial position
        // We can't get mouse position here easily without an event, but mousemove will fire immediately after
    }

    private hidePreview() {
        if (this.componentRef) {
            this.appRef.detachView(this.componentRef.hostView);
            this.componentRef.destroy();
            this.componentRef = null;
        }
    }

    private updatePosition(event: MouseEvent) {
        if (!this.componentRef) return;

        const domElem = (this.componentRef.hostView as EmbeddedViewRef<any>).rootNodes[0] as HTMLElement;

        // Position to the right of the cursor by default
        let top = event.clientY + 20;
        let left = event.clientX + 20;

        // Check bounds
        const rect = domElem.getBoundingClientRect();
        const viewportWidth = window.innerWidth;
        const viewportHeight = window.innerHeight;

        if (left + rect.width > viewportWidth) {
            left = event.clientX - rect.width - 20; // Flip to left
        }

        if (top + rect.height > viewportHeight) {
            top = event.clientY - rect.height - 20; // Flip to top
        }

        domElem.style.top = `${top}px`;
        domElem.style.left = `${left}px`;
    }

    ngOnDestroy() {
        this.hidePreview();
    }
}
