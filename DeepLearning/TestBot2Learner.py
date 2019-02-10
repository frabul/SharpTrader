import os
import json
import msvcrt as ms
import numpy as np
from random import shuffle
import keras
from keras.models import Sequential
from keras.layers import Dense, Dropout, Activation, Flatten
from keras.layers import Conv2D, MaxPooling3D, MaxPooling2D
from keras.callbacks import Callback

from enum import Enum, unique


class PerformaceStats:
    '''Calculates stats'''
    def __init__(self, predicted, correct, wonstream):
        self.CorrectPos = 0
        self.WrongPos = 0
        self.CorrectNeg = 0
        self.WrongNeg = 0
        self.Sharpe = 0
        self.Won = 0
        self.MaxDrawDown = 0
        y = predicted
        length = 0
        if type(y) is np.ndarray:
            length = y.shape[0]
        else:
            length = len(y)
        peakWon = 0 
        profSumSq = 0
        for i in range(length): 
            if y[i][1] > y[i][0]:  # predict buy
                self.Won += (wonstream[i] - 0.002)
                profSumSq += (wonstream[i] - 0.002) * (wonstream[i] - 0.002) 
                peakWon = max(self.Won, peakWon)
                self.MaxDrawDown = max(peakWon - self.Won, self.MaxDrawDown)
                if correct[i][1] > correct[i][0] and  correct[i][1] >= 0.5:
                    self.CorrectPos += 1
                else:
                    self.WrongPos += 1
            else:
                if correct[i][1] > correct[i][0]:
                    self.WrongNeg += 1
                else:
                    self.CorrectNeg += 1

        self.Positives = self.CorrectPos + self.WrongPos
        self.Negatives = self.WrongNeg + self.CorrectNeg
        self.ProfitFactor = self.CorrectPos / \
            self.WrongPos if self.WrongPos != 0 else 1000
        self.Accuracy = self.CorrectPos / self.Positives if self.Positives != 0 else 0
        self.ProfPerTrade = self.Won / self.Positives

    def print(self):
        print(f"   Positives count: {self.Positives} - Profit factor: {self.ProfitFactor} - Accuracy {self.Accuracy}"
              + f"\n   Won: {self.Won} - Profit/trade {self.ProfPerTrade} - DD: {self.MaxDrawDown}")


def CheckKeyPress(comm):
    keys = ""
    if ms.kbhit():
        while ms.kbhit():
            keys += ms.getwch()
        text = input(f"You pressed a key - write line {comm} to confirm")
        if text == comm:
            return True
        return False
    return False

#-------------- class NBatchLogger ---------------
class NBatchLogger(Callback):
    ''' display: Number of batches to wait before outputting loss '''
    def __init__(self, display=100):
        self.seen = 0
        self.display = display

    def on_epoch_end(self, epoch, logs ):
        self.seen += 1
        if self.seen % self.display == 0:
            if CheckKeyPress('stop'):
                self.model.stop_training = True
            totEpochos = self.params['epochs']
            outStr = f'\nEpoch {self.seen}/{totEpochos} - '
            for k, v in logs.items():
                outStr += (str(k) + ": " + str(v) + "  ")
            print(outStr)
            perfStats = PerformaceStats(model.predict(xTest), yTest, wonTest)
            perfStats.print()
#-------------- NBatchLogger END ---------------

#-----------------------------------------------
save_dir = "./"
model_name = "MyFirstModel"

fs = open("d:\\dataset.json", "r")
data = json.load(fs)
# shuffle(data) 

#featuresList = [[d['Features'][0],d['Features'][2]]  for d in data]
featuresList = [[d['Features'][0],d['Features'][1]]  for d in data]
labelsList = [d['Labels'] for d in data]
outputs = [la[0] for la in labelsList]
wonList = [la[1] for la in labelsList]

#divide data in test and train sets
sp = 25 #span between test and train
sampleNum = len(featuresList)
trainLen = int(len(featuresList) * 4 / 5)
testLen = sampleNum - trainLen
trainFirstPart = (0, int(trainLen/2))
testSpan = (trainFirstPart[1] + sp, trainFirstPart[1] + sp + testLen)
trainSecondPart = (testSpan[1] + sp, sampleNum)

xTrain = np.array(featuresList[trainFirstPart[0]:trainFirstPart[1]] +
                  featuresList[trainSecondPart[0]:trainSecondPart[1]])

yTrain = np.array(outputs[trainFirstPart[0]:trainFirstPart[1]] +
                  outputs[trainSecondPart[0]:trainSecondPart[1]])
wonTrain = np.array(wonList[trainFirstPart[0]:trainFirstPart[1]] +
                    wonList[trainSecondPart[0]:trainSecondPart[1]])
xTest = np.array(featuresList[testSpan[0]:testSpan[1]])
yTest = np.array(outputs[testSpan[0]:testSpan[1]]) 
wonTest = np.array(wonList[testSpan[0]:testSpan[1]])

# labelse have shape [sample, result, won]
# we need two arrays [sample, result] [sample, won]
# labelse have shape [sample, result, won]
# we need two arrays [sample, result] [sample, won]

xTrain = xTrain.astype('float32')
xTest = xTest.astype('float32')
yTrain = yTrain.astype('float32')
ytest = yTest.astype('float32')
yTrain = keras.utils.to_categorical(yTrain, 2).astype('float32')
yTest = keras.utils.to_categorical(yTest, 2).astype('float32')

stats = PerformaceStats([[0, 1] for i in range(yTrain.shape[0])], yTrain, wonTrain)
stats.print()
print("\n###TEST STATS#####")
stats = PerformaceStats([[0, 1] for i in range(yTest.shape[0])], yTest, wonTest)
stats.print()

#------------------
model = Sequential()
model.add(Conv2D(16, (1, 3), padding='valid',
                 input_shape=xTrain.shape[1:]))
model.add(Activation('relu'))
model.add(Dropout(0.1))
model.add(Conv2D(32, (2, 1), padding='same'))
model.add(Activation('relu'))
#model.add(Dropout(0.1))
model.add(Flatten())
model.add(Dense(128))
model.add(Activation('relu'))
#model.add(Dropout(0.1))
model.add(Dense(2))
model.add(Activation('softmax'))
 
#opt = keras.optimizers.rmsprop(lr=0.0005, decay=1e-9)
opt = keras.optimizers.Adam(lr=0.001, decay=1e-8)

model.compile(loss='categorical_crossentropy',
              optimizer=opt,
              metrics=['accuracy']) 
###### TRAIN #####
model.fit(xTrain, yTrain,
          #batch_size=xTrain.shape[0],
          batch_size=500,
          epochs=50000,
          validation_data=(xTest, yTest),
          shuffle=False,
          verbose=0,
          callbacks=[NBatchLogger(10), ]
         )

# Save model and weights
if not os.path.isdir("./"):
    os.makedirs("./")
model_path = os.path.join(save_dir, model_name)
model.save(model_path)
print('Saved trained model at %s ' % model_path)

# Score trained model.
scores = model.evaluate(xTest, yTest, verbose=1)
print('Test loss:', scores[0])
print('Test accuracy:', scores[1])
