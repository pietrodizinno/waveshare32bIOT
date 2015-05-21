﻿using DisplayFont;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Windows.Devices.Enumeration;
using Windows.Devices.Gpio;
using Windows.Devices.Spi;

namespace ili9340
{
    public class Cili9340
    {
        private const string SPI_CONTROLLER_NAME = "SPI0";
        private const Int32 SPI_CHIP_SELECT_LINE = 0;
        private const Int32 DATA_COMMAND_PIN = 22;
        private const Int32 RESET_PIN = 27;

        // Based on ili9340, waveshare 3.2 v4
        public const UInt32 SCREEN_WIDTH_PX = 320;                         /* Number of horizontal pixels on the display */
        public const UInt32 SCREEN_HEIGHT_PX = 240;                         /* Number of vertical pixels on the display   */
        private const UInt32 PIXELS_PER_PAGE = 8;
        private const UInt32 SCREEN_HEIGHT_PAGES = SCREEN_HEIGHT_PX / PIXELS_PER_PAGE;    /* The vertical pixels on this display are arranged into 'pages' of 8 pixels each */
        private byte[,] DisplayBuffer = new byte[SCREEN_WIDTH_PX, SCREEN_HEIGHT_PX];                 /* A local buffer we use to store graphics data for the screen                    */
        private byte[] SerializedDisplayBuffer = new byte[SCREEN_WIDTH_PX * SCREEN_HEIGHT_PX];                /* A temporary buffer used to prepare graphics data for sending over SPI          */

        /* Definitions for SPI and GPIO */
        private SpiDevice SpiDisplay;       // lcd
        private GpioController IoController;
        private GpioPin DataCommandPin;
        private GpioPin ResetPin;

        /* Display commands. See the datasheet for details on commands: http://www.adafruit.com/datasheets/SSD1306.pdf                      */
        private static readonly byte[] CMD_DISPLAY_OFF = { 0x28 };              /* Turns the display off                                    */
        private static readonly byte[] CMD_DISPLAY_ON = { 0x29 };               /* Turns the display on                                     */
        private static readonly byte[] CMD_RESETCOLADDR = { 0x2a, 0x00, 0x00, 0x00, 0xEF }; /* Reset the column address pointer                         */
        private static readonly byte[] CMD_RESETPAGEADDR = { 0x2b, 0x00, 0x00, 0x01, 0x3F };/* Reset the page address pointer                           */
        private const byte MADCTL = 0x36;
        private const byte MADCTL_MY = 0x80;
        private const byte MADCTL_MX = 0x40;
        private const byte MADCTL_MV = 0x20;
        private const byte MADCTL_ML = 0x10;
        private const byte MADCTL_RGB = 0x00;
        private const byte MADCTL_BGR = 0x08;
        private const byte MADCTL_MH = 0x04;
        private const byte PIXFMT = 0x3A;
        private const byte PWCTR1 = 0xC0;
        private const byte PWCTR2 = 0xC1;
        private const byte VMCTR1 = 0xC5;
        private const byte VMCTR2 = 0xC7;
        private const byte CASET = 0x2a;
        private const byte PASET = 0x2b;
        private const byte RAMWR = 0x2C;
        private const byte INVOFF = 0x20;
        private const byte INVON = 0x21;

        private uint _width = 0;
        private uint _height = 0;



        public Cili9340() { }

        public async void InitAll()
        {
            try {       
                InitGpioLCD();
                await InitSpiLCD();        //Initialize the SPI controller               
                await InitDisplay();    //Initialize the display  
            } catch (Exception ex)
            {
                Debug.WriteLine(ex.Message);
            }
        }

        private void InitGpioLCD()
        {
            try
            {
                Debug.WriteLine("start of InitGpioLCD");
                IoController = GpioController.GetDefault(); /* Get the default GPIO controller on the system */

            /* Initialize a pin as output for the Data/Command line on the display  */
            DataCommandPin = IoController.OpenPin(DATA_COMMAND_PIN);
                DataCommandPin.Write(GpioPinValue.High);
                DataCommandPin.SetDriveMode(GpioPinDriveMode.Output);

                /* Initialize a pin as output for the hardware Reset line on the display */
                 ResetPin = IoController.OpenPin(RESET_PIN);
                ResetPin.Write(GpioPinValue.High);
                ResetPin.SetDriveMode(GpioPinDriveMode.Output);


                Debug.WriteLine("End of InitGpioLCD");
            }
            /* If initialization fails, throw an exception */
            catch (Exception ex)
            {
                throw new Exception("InitGpioLCD initialization failed", ex);
            }
        }

        private async Task InitSpiLCD()
        {

            try
            {
                Debug.WriteLine("Start of InitSpiLCD");
                var settings = new SpiConnectionSettings(SPI_CHIP_SELECT_LINE); /* Create SPI initialization settings                               */
                settings.ClockFrequency = 48000000;                             /* Datasheet specifies maximum SPI clock frequency of 16MHz         */
                //settings.DataBitLength = 8;
                settings.Mode = SpiMode.Mode0;                                  /* The display expects an idle-high clock polarity, we use Mode3    
                                                                                 * to set the clock polarity and phase to: CPOL = 1, CPHA = 1         
                                                                                 */


                string spiAqs = SpiDevice.GetDeviceSelector(SPI_CONTROLLER_NAME);       /* Find the selector string for the SPI bus controller          */
                var devicesInfo = await DeviceInformation.FindAllAsync(spiAqs);         /* Find the SPI bus controller device with our selector string  */
                SpiDisplay = await SpiDevice.FromIdAsync(devicesInfo[0].Id, settings);  /* Create an SpiDevice with our bus controller and SPI settings */
                Debug.WriteLine("End of InitSpiLCD");
            }
            /* If initialization fails, display the exception and stop running */
            catch (Exception ex)
            {
                throw new Exception("InitSpiLCD Initialization Failed", ex);
            }
        }

        /* Send SPI commands to power up and initialize the display */
        private async Task InitDisplay()
        {
            /* Initialize the display */
            try
            {
                // Display Off
                //DisplaySendCommand(CMD_DISPLAY_OFF);

                await ResetDisplay();                   /* Perform a hardware reset on the display                                  */

                // whats this?  this is in adafruit
                DisplaySendData(0xEF);
                DisplaySendData(new byte[] { 0x03, 0x80, 0x02 });

                DisplaySendData(0xCB);
                DisplaySendData(new byte[] { 0x39, 0x2C, 0x00, 0x34, 0x02 });

                DisplaySendCommand(0xCF);
                DisplaySendData(new byte[] { 0x00, 0xC1, 0x30 });
                DisplaySendCommand(0xE8);
                DisplaySendData(new byte[] { 0x85, 0x00, 0x78 });
                DisplaySendCommand(0xEA);
                DisplaySendData(new byte[] { 0x00, 0x00 });
                DisplaySendCommand(0xED);
                DisplaySendData(new byte[] { 0x64, 0x03, 0x12, 0x81 });

                DisplaySendCommand(0xF7);
                DisplaySendData(0x20);

                // Power Control 1 
                DisplaySendCommand(PWCTR1);
                DisplaySendData(0x23);
                //TFT_24S_Write_Command(0x00C0);TFT_24S_Write_Data(0x0026);TFT_24S_Write_Data(0x0004);	//power control 1

                // Power Control 2
                DisplaySendCommand(PWCTR2);
                DisplaySendData(0x10);
                //DisplaySendData(0x04);
                //TFT_24S_Write_Command(0x00C1);TFT_24S_Write_Data(0x0004);

                // VCOM Control 1 
                DisplaySendCommand(VMCTR1);
                DisplaySendData(new byte[] { 0x3e, 0x28 });
                //DisplaySendData(new byte[] { 0x34, 0x40 });
                //TFT_24S_Write_Command(0x00C5);TFT_24S_Write_Data(0x0034);TFT_24S_Write_Data(0x0040)

                // VCOM Control 2 
                DisplaySendCommand(VMCTR2);
                DisplaySendData(0x86);
                //DisplaySendData(0xC0);
                //TFT_24S_Write_Command(0x00C7);TFT_24S_Write_Data(0x00C0);				//VCOM control 2


                // Memory access control = bgr + landscape     

                RotateScreen(0);
                //DisplaySendData(0x88);
                //TFT_24S_Write_Command(0x0036); TFT_24S_Write_Data(0x0088);

                // COLMOD: Pixel Format Set (16 bit per pixel)
                DisplaySendCommand(PIXFMT);
                DisplaySendData(0x55);
                //DisplaySendData(0x66);

                // Frame Rate Control 
                // Division ratio = fosc, Frame Rate = 79Hz
                DisplaySendCommand(0xB1);
                DisplaySendData(new byte[] { 0x00, 0x18 });
                //DisplaySendData(new byte[] { 0x00, 0x16 });

                // Display Function Control 
                DisplaySendCommand(0xB6);
                DisplaySendData(new byte[] { 0x08, 0x82, 0x27 });
                //DisplaySendData(new byte[] { 0x0A, 0x82, 0x27 });

                // Gamma Function Disable 
                DisplaySendCommand(0xF2);
                DisplaySendData(0x00);

                // Gamma curve selected  
                DisplaySendCommand(0x26);
                DisplaySendData(0x01);

                // Positive Gamma Correction
                DisplaySendCommand(0xE0);
                DisplaySendData(new byte[] { 0x0F, 0x31, 0x2B, 0x0C, 0x0E, 0x08, 0x4E, 0xF1, 0x37, 0x07, 0x10, 0x03, 0x0E, 0x09, 0x00 });

                // Negative Gamma Correction 
                DisplaySendCommand(0xE1);
                DisplaySendData(new byte[] { 0x00, 0x0E, 0x14, 0x03, 0x11, 0x07, 0x31, 0xC1, 0x48, 0x08, 0x0F, 0x0C, 0x31, 0x36, 0x0F });

                // Sleep OUT ?
                DisplaySendCommand(0x11);

                // sleep
                await Task.Delay(120);

                // Display ON 
                DisplaySendCommand(CMD_DISPLAY_ON);

                

                

                //DisplaySendCommand(0x2c);

                // Rotate display
                //writecommand(0x36);
                //writedata(0x20 | 0x08);


                // Display brighness value
                //DisplaySendCommand(0x51);
                //DisplaySendData(0x22);

                Debug.WriteLine("End of InitDisplay");

            }
            catch (Exception ex)
            {
                throw new Exception("Display Initialization Failed", ex);
            }
        }

        public void RotateScreen(int rotation)
        {


            DisplaySendCommand(MADCTL);
            switch (rotation)
            {
                case 0:
                    DisplaySendData(MADCTL_MX | MADCTL_BGR);
                    _width = SCREEN_WIDTH_PX;
                    _height = SCREEN_HEIGHT_PX;
                    break;
                case 1:
                    DisplaySendData(MADCTL_MV | MADCTL_BGR);
                    _width = SCREEN_HEIGHT_PX;
                    _height = SCREEN_WIDTH_PX;
                    break;
                case 2:
                    DisplaySendData(MADCTL_MY | MADCTL_BGR);
                    _width = SCREEN_WIDTH_PX;
                    _height = SCREEN_HEIGHT_PX;
                    break;
                case 3:
                    DisplaySendData(MADCTL_MX | MADCTL_MY | MADCTL_MV | MADCTL_BGR);
                    _width = SCREEN_HEIGHT_PX;
                    _height = SCREEN_WIDTH_PX;
                    break;
            }
        }

        public void PushColor(uint color)
        {
            DisplaySendData(new byte[] { (byte)(color >> 8), (byte)(color & 0xff) });
        }


        public void DrawPixel(uint x, uint y, uint color)
        {
            if ((x < 0) || (x >= _width) || (y < 0) || (y >= _height)) return;
            addressSet(x, y, (x + 1), (y + 1));
            PushColor(color);
//            Debug.WriteLine("x=" + x + " y=" + y);
        }

        public void DrawFastVLine(uint x, uint y, uint h, uint color)
        {
            if ((x >= _width) || (y >= _height)) return;
            if ((y + h - 1) >= _height)
                h = _height - y;
            addressSet(x, y, x, (y + h+1));
            while (h-- > 0)
            {
                PushColor(color);
            }
        }

        public void DrawFastHLine(uint x, uint y, uint w, uint color)
        {
            if ((x >= _width) || (y >= _height)) return;
            if ((x + w - 1) >= _width) w = _width - x;
            addressSet(x, y, x+w-1, y);
            while (w-- > 0)
            {
                PushColor(color);
            }
        }

        public void FillScreen(uint color)
        {
            DrawFillRect(0, 0, _width, _height, color);
        }

        public void DrawFillRect(uint x, uint y, uint w, uint h, uint color)
        {
            if ((x >= _width) || (y >= _height)) return;
            if ((x + w - 1) >= _width) w = _width - x;
            if ((y + h - 1) >= _height) h = _height - y;

            addressSet(x, y, (x+w-1), (y+h-1));
            for (y = h; y > 0; y--)
            {
                for (x = w; x > 0; x--)
                {
                    PushColor(color);
                }
            }
        }

        /* Perform a hardware reset of the display */
        private async Task ResetDisplay()
        {
            ResetPin.Write(GpioPinValue.Low);   /* Put display into reset                       */
            await Task.Delay(1);                /* Wait at least 3uS (We wait 1mS since that is the minimum delay we can specify for Task.Delay() */
            ResetPin.Write(GpioPinValue.High);  /* Bring display out of reset                   */
            await Task.Delay(100);              /* Wait at least 100mS before sending commands  */


        }


        /* Send graphics data to the screen */
        private void DisplaySendData(byte[] Data)
        {
            /* When the Data/Command pin is high, SPI data is treated as graphics data  */
            DataCommandPin.Write(GpioPinValue.High);
            SpiDisplay.Write(Data);
        }

        private void DisplaySendData(byte data)
        {
            DataCommandPin.Write(GpioPinValue.High);
            SpiDisplay.Write(new byte[] { data });
        }

        private void DisplaySendData(ushort data)
        {
            var data1 = (byte)(data >> 8);
            var data2 = (byte)(data & 0xff);

            DataCommandPin.Write(GpioPinValue.High);
            SpiDisplay.Write(new byte[] { data1, data2 });
        }

        /* Send commands to the screen */
        private void DisplaySendCommand(byte command)
        {
            /* When the Data/Command pin is low, SPI data is treated as commands for the display controller */
            DataCommandPin.Write(GpioPinValue.Low);
            SpiDisplay.Write(new byte[] { command });
        }

        private void DisplaySendCommand(byte[] command)
        {
            /* When the Data/Command pin is low, SPI data is treated as commands for the display controller */
            DataCommandPin.Write(GpioPinValue.Low);
            SpiDisplay.Write(command);
        }

        private void ReadCommand(byte command)
        {
            /* When the Data/Command pin is low, SPI data is treated as commands for the display controller */
            DataCommandPin.Write(GpioPinValue.Low);
            SpiDisplay.Read(new byte[] { command });
        }


        private void addressSet(uint x1, uint y1, uint x2, uint y2)
        {
            DisplaySendCommand(CASET);
            DisplaySendData(new byte[] { (byte)(x1 >> 8), (byte)(x1), (byte)(x2 >> 8), (byte)(x2) });

            DisplaySendCommand(PASET);
            DisplaySendData(new byte[] { (byte)(y1 >> 8), (byte)(y1), (byte)(y2 >> 8), (byte)(y2) });

            DisplaySendCommand(RAMWR);
        }

        public void InvertDisplay(Boolean i)
        {
            DisplaySendCommand(i ? INVON : INVOFF);
        }


        public void Dispose(object sender, object args)
        {
            /* Cleanup */
            SpiDisplay.Dispose();
            ResetPin.Dispose();

            DataCommandPin.Dispose();
            Debug.WriteLine("unloaded");
        }





        /*----- CODE DOES WORK FROM HERE FOR NOW ---------*/

        public void ClearScreen(int color)
        {
            //DisplaySendCommand(0x2C);

            var hcolor = (byte)(color >> 8);
            var lcolor = (byte)(color & 0xff);
            var data = new List<byte>();

            //for (int i = 0; i < 32000; i++)
            //{
            //    data.Add(hcolor);
            //    data.Add(lcolor);
            //data.Add(hcolor);
            //}

            for (int PageY = 0; PageY < SCREEN_HEIGHT_PX; PageY++)
            {
                for (int PixelX = 0; PixelX < SCREEN_WIDTH_PX; PixelX++)
                {
                    DisplayBuffer[PixelX, PageY] = hcolor;
                }
            }

            /*
            addressSet(0, 0, (short)(SCREEN_WIDTH_PX - 1), (short)(SCREEN_HEIGHT_PX - 1));
            for (int i = 0; i < SCREEN_HEIGHT_PX; i++)
            {
                for (int j = 0; j < SCREEN_WIDTH_PX; j++)                       //256bytes = 128pixels/line
                {
                    data.Add(hcolor);
                    data.Add(hcolor);
                    data.Add(lcolor);
                    DisplaySendData(new byte[] { hcolor, lcolor });
                }
            }*/

            //DisplaySendData(data.ToArray());
            //DisplayBuffer[]
            // DisplaySendCommand(0x2C);

        }


        ///* Writes the Display Buffer out to the physical screen for display */
        public void DisplayUpdate()
        {
            int Index = 0;
            /* We convert our 2-dimensional array into a serialized string of bytes that will be sent out to the display */
            for (int PageY = 0; PageY < SCREEN_HEIGHT_PX; PageY++)
            {
                for (int PixelX = 0; PixelX < SCREEN_WIDTH_PX; PixelX++)
                {
                    SerializedDisplayBuffer[Index] = DisplayBuffer[PixelX, PageY];
                    Index++;
                }
            }

            /* Write the data out to the screen */
            //DisplaySendCommand(CMD_RESETCOLADDR);         /* Reset the column address pointer back to 0 */
            //DisplaySendCommand(CMD_RESETPAGEADDR);        /* Reset the page address pointer back to 0   */
            //DisplaySendCommand(0x2C);
            addressSet(0, 0, 320, 240);
            DisplaySendData(SerializedDisplayBuffer);     /* Send the data over SPI */  
            DisplaySendCommand(0x2C);
        }



        public void DrawText(uint x, uint y, string Line, uint color)
        {
            foreach (Char Character in Line)
            {
                FontCharacterDescriptor CharDescriptor = DisplayFontTable.GetCharacterDescriptor(Character);
                if (CharDescriptor == null)
                {
                    return;
                }
                uint w = CharDescriptor.CharacterWidthPx;
                w = 16;
                uint h = CharDescriptor.CharacterHeightBytes;
                addressSet(x, y, x + w, y + h);
                DisplaySendData(CharDescriptor.CharacterData);
                x += w;
            }
         

        }


        /* 
         * NAME:        WriteLineDisplayBuf
         * DESCRIPTION: Writes a string to the display screen buffer (DisplayUpdate() needs to be called subsequently to output the buffer to the screen)
         * INPUTS:
         *
         * Line:      The string we want to render. In this sample, special characters like tabs and newlines are not supported.
         * Col:       The horizontal column we want to start drawing at. This is equivalent to the 'X' axis pixel position.
         * Row:       The vertical row we want to write to. The screen is divided up into 4 rows of 16 pixels each, so valid values for Row are 0,1,2,3.
         *
         * RETURN VALUE:
         * None. We simply return when we encounter characters that are out-of-bounds or aren't available in the font.
         */
        private void WriteLineDisplayBuf(String Line, UInt32 Col, UInt32 Row)
        {
            UInt32 CharWidth = 0;
            foreach (Char Character in Line)
            {
                CharWidth = WriteCharDisplayBuf(Character, Col, Row);
                Col += CharWidth;   /* Increment the column so we can track where to write the next character   */
                if (CharWidth == 0) /* Quit if we encounter a character that couldn't be printed                */
                {
                    return;
                }
            }
        }

        /* 
         * NAME:        WriteCharDisplayBuf
         * DESCRIPTION: Writes one character to the display screen buffer (DisplayUpdate() needs to be called subsequently to output the buffer to the screen)
         * INPUTS:
         *
         * Character: The character we want to draw. In this sample, special characters like tabs and newlines are not supported.
         * Col:       The horizontal column we want to start drawing at. This is equivalent to the 'X' axis pixel position.
         * Row:       The vertical row we want to write to. The screen is divided up into 4 rows of 16 pixels each, so valid values for Row are 0,1,2,3.
         *
         * RETURN VALUE:
         * We return the number of horizontal pixels used. This value is 0 if Row/Col are out-of-bounds, or if the character isn't available in the font.
         */
        private UInt32 WriteCharDisplayBuf(Char Chr, UInt32 Col, UInt32 Row)
        {
            /* Check that we were able to find the font corresponding to our character */
            FontCharacterDescriptor CharDescriptor = DisplayFontTable.GetCharacterDescriptor(Chr);
            if (CharDescriptor == null)
            {
                return 0;
            }

            /* Make sure we're drawing within the boundaries of the screen buffer */
            UInt32 MaxRowValue = (SCREEN_HEIGHT_PAGES / DisplayFontTable.FontHeightBytes) - 1;
            UInt32 MaxColValue = SCREEN_WIDTH_PX;
            if (Row > MaxRowValue)
            {
                return 0;
            }
            if ((Col + CharDescriptor.CharacterWidthPx + DisplayFontTable.FontCharSpacing) > MaxColValue)
            {
                return 0;
            }

            UInt32 CharDataIndex = 0;
            UInt32 StartPage = Row * 2;
            UInt32 EndPage = StartPage + CharDescriptor.CharacterHeightBytes;
            UInt32 StartCol = Col;
            UInt32 EndCol = StartCol + CharDescriptor.CharacterWidthPx;
            UInt32 CurrentPage = 0;
            UInt32 CurrentCol = 0;

            /* Copy the character image into the display buffer */
            for (CurrentPage = StartPage; CurrentPage < EndPage; CurrentPage++)
            {
                for (CurrentCol = StartCol; CurrentCol < EndCol; CurrentCol++)
                {
                    DisplayBuffer[CurrentCol, CurrentPage] = CharDescriptor.CharacterData[CharDataIndex];
                    CharDataIndex++;
                }
            }

            /* Pad blank spaces to the right of the character so there exists space between adjacent characters */
            for (CurrentPage = StartPage; CurrentPage < EndPage; CurrentPage++)
            {
                for (; CurrentCol < EndCol + DisplayFontTable.FontCharSpacing; CurrentCol++)
                {
                    DisplayBuffer[CurrentCol, CurrentPage] = 0x00;
                }
            }

            /* Return the number of horizontal pixels used by the character */
            return CurrentCol - StartCol;
        }

        /* Sets all pixels in the screen buffer to 0 */
        private void ClearDisplayBuf()
        {
            Array.Clear(DisplayBuffer, 0, DisplayBuffer.Length);
        }

        /* Update the SPI display to mirror the textbox contents */
        public void DisplayHelloText()
        {
            try
            {
                //Debug.WriteLine("start DisplayHelloText");
                //ClearDisplayBuf();  /* Blank the display buffer             */
                WriteLineDisplayBuf("HELLO!", 100,100);


                //Debug.WriteLine("end DisplayHelloText");
            }
            /* Show an error if we can't update the display */
            catch (Exception ex)
            {
                //Text_Status.Text = "Status: Failed to update display";
                //Text_Status.Text = "\nException: " + ex.Message;
            }
        }

    }
}
